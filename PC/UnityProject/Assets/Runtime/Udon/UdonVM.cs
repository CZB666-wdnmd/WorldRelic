using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalVrcw.Udon
{
    /// <summary>
    /// Udon bytecode interpreter. Stack holds heap addresses; values live in heap.
    /// Instruction encoding: big-endian u32 opcode, optional big-endian u32 operand.
    /// </summary>
    public class UdonVM
    {
        public enum Op : uint { Nop = 0, Push = 1, Pop = 2, JumpIfFalse = 4, Jump = 5, Extern = 6, Annotation = 7, JumpIndirect = 8, Copy = 9 }

        public UdonRuntime runtime;
        public RawProgram prog;
        public Dictionary<uint, object> heap = new Dictionary<uint, object>();
        public Stack<uint> stack = new Stack<uint>();
        public bool guardHit;

        public UdonVM(UdonRuntime rt, RawProgram program)
        {
            runtime = rt;
            prog = program;
            // initial heap from program constants
            runtime.ResetConstantReader();
            foreach (var kv in program.heapMap)
                heap[kv.Key] = runtime.ConvertConst(kv.Key, kv.Value);
        }

        public void Reset()
        {
            heap.Clear();
            runtime.ResetConstantReader();
            foreach (var kv in prog.heapMap)
                heap[kv.Key] = runtime.ConvertConst(kv.Key, kv.Value);
            stack.Clear();
            // Constants are only half of an Udon instance's initial state.
            // Restore the serialized public variables as well; otherwise a
            // reset silently turns every object reference into null.
            if (runtime != null) runtime.ApplyVariables();
        }

        public bool RunUpdateLimited()
        {
            if (!prog.entryMap.TryGetValue("_update", out uint entry)) return false;
            guardHit = false;
            RunAt(entry, MaxUpdateMs);
            return guardHit;
        }

        public bool TryRun(string eventName)
        {
            if (prog.entryMap.TryGetValue(eventName, out uint entry))
            {
                RunAt(entry);
                return true;
            }
            return false;
        }

        // Event calls can nest through SendCustomEvent. A per-call stopwatch
        // allowed each nested call to restart the budget, so a cycle could
        // still block the Unity main thread for a long time. Share one budget
        // across the complete event tree.
        static readonly System.Diagnostics.Stopwatch evtTimer = new System.Diagnostics.Stopwatch();
        static int executionDepth;
        static int executionInstructions;
        static bool executionBudgetHit;
        static UdonRuntime executionOwner;
        static UdonVM activeVm;
        // A cold Unity frame can spend more than 25ms resolving the first
        // renderer/audio/physics extern. The reconstructed programs are
        // finite; allow their original bytecode path to finish while the
        // instruction/depth/update suspension guards still bound bad data.
        public const long MaxEventMs = 60;
        public const long MaxUpdateMs = 60;
        public const int MaxInstructions = 100000;
        static long executionBudgetMs;

        public void RunAt(uint start)
        {
            RunAt(start, MaxEventMs);
        }

        void RunAt(uint start, long budgetMs)
        {
            bool root = executionDepth == 0;
            if (root)
            {
                evtTimer.Restart();
                executionInstructions = 0;
                executionBudgetHit = false;
                executionOwner = runtime;
                executionBudgetMs = budgetMs;
            }

            UdonVM callerVm = activeVm;
            uint[] callerStack = null;
            if (ReferenceEquals(callerVm, this)) callerStack = stack.ToArray();
            activeVm = this;
            executionDepth++;

            guardHit = false;
            stack.Clear();
            byte[] bc = prog.bytecode;
            uint pc = start;
            int guard = 0;
            try
            {
                while (!executionBudgetHit && pc < bc.Length && guard++ < MaxInstructions &&
                       executionInstructions < MaxInstructions && evtTimer.ElapsedMilliseconds < executionBudgetMs)
                {
                    executionInstructions++;
                    uint instrAddr = pc;
                    uint op = ReadU32(bc, pc);
                    pc += 4;
                    uint operand = 0;
                    bool hasOperand = HasOperand((Op)op);
                    if (hasOperand)
                    {
                        operand = ReadU32(bc, pc);
                        pc += 4;
                    }

                    switch ((Op)op)
                    {
                        case Op.Nop:
                        case Op.Annotation:
                            break;
                        case Op.Push:
                            stack.Push(operand);
                            break;
                        case Op.Pop:
                            if (stack.Count > 0) stack.Pop();
                            break;
                        case Op.Copy:
                            {
                                if (stack.Count < 2) break;
                                uint target = stack.Pop();
                                uint source = stack.Pop();
                                heap[target] = heap.TryGetValue(source, out var sv) ? sv : null;
                                break;
                            }
                        case Op.Jump:
                            pc = operand;
                            break;
                        case Op.JumpIfFalse:
                            {
                                if (stack.Count > 0)
                                {
                                    uint a = stack.Pop();
                                    if (IsFalsy(Get(a))) pc = operand;
                                }
                                break;
                            }
                        case Op.Extern:
                            {
                                object sigObj = Get(operand);
                                string sig = sigObj as string;
                                if (sig == null) { runtime.LogError("EXTERN no signature @" + operand); return; }
                                if (!UdonExterns.TryExtern(this, sig, out uint? newPc, instrAddr))
                                {
                                    // unsupported extern: treat as no-op but keep stack balanced
                                    runtime.LogError("UNSUPPORTED extern: " + sig);
                                    return;
                                }
                                if (newPc.HasValue) pc = newPc.Value;
                                break;
                            }
                        case Op.JumpIndirect:
                            {
                                object target = Get(operand);
                                uint t = target is uint u ? u : (target is int i ? (uint)i : 0u);
                                if (t == 0xFFFFFFFFu || t >= bc.Length) return; // return jump
                                pc = t;
                                break;
                            }
                    }
                }

                if (executionBudgetHit || guard >= MaxInstructions || executionInstructions >= MaxInstructions ||
                    evtTimer.ElapsedMilliseconds >= executionBudgetMs)
                {
                    executionBudgetHit = true;
                    guardHit = true;
                }
            }
            finally
            {
                if (callerStack != null)
                {
                    stack.Clear();
                    for (int i = callerStack.Length - 1; i >= 0; i--) stack.Push(callerStack[i]);
                }
                activeVm = callerVm;
                executionDepth--;
                if (root)
                {
                    if (executionBudgetHit)
                        executionOwner.LogError("VM event budget hit @ " + start.ToString("X") +
                                                " instructions=" + executionInstructions);
                    evtTimer.Stop();
                    executionOwner = null;
                    executionBudgetHit = false;
                    executionBudgetMs = 0;
                }
            }
        }

        public object Get(uint addr)
        {
            return heap.TryGetValue(addr, out object v) ? v : null;
        }

        public void Set(uint addr, object value)
        {
            heap[addr] = value;
        }

        static bool IsFalsy(object v)
        {
            if (v == null) return true;
            if (v is bool b) return !b;
            if (v is int i) return i == 0;
            if (v is float f) return f == 0f;
            if (v is double d) return d == 0;
            if (v is long l) return l == 0;
            if (v is uint u) return u == 0;
            return false;
        }

        public static bool HasOperand(Op op)
        {
            return op == Op.Push || op == Op.JumpIfFalse || op == Op.Jump ||
                   op == Op.Extern || op == Op.Annotation || op == Op.JumpIndirect;
        }

        public static uint ReadU32(byte[] b, uint off)
        {
            if (off > b.Length || b.Length - off < 4) throw new System.IO.InvalidDataException("Truncated Udon instruction");
            return ((uint)b[off] << 24) | ((uint)b[off + 1] << 16) | ((uint)b[off + 2] << 8) | b[off + 3];
        }
    }
}


