using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LocalVrcw.Udon
{
    // Private CPU geometry derived from the user's GPU buffers at runtime.
    // No exported mesh files, canned collider shapes or bundle mutations.
    public static class RuntimeMeshReadback
    {
        static readonly Dictionary<Mesh, Mesh> copies = new Dictionary<Mesh, Mesh>();
        public static int Count => copies.Count;
        public static Mesh Readable(Mesh source)
        {
            if (source == null || source.isReadable) return source;
            if (copies.TryGetValue(source, out var cached)) return cached;
            if (!SystemInfo.supportsAsyncGPUReadback) throw new InvalidOperationException("Exact imported mesh queries require GPU readback on this Windows backend");
            int count = source.vertexCount;
            if (count <= 0 || count > 1000000) throw new InvalidOperationException("Imported mesh vertex limit");
            var format = source.GetVertexAttributeFormat(VertexAttribute.Position);
            int dimension = source.GetVertexAttributeDimension(VertexAttribute.Position);
            int stream = source.GetVertexAttributeStream(VertexAttribute.Position);
            int stride = source.GetVertexBufferStride(stream), offset = source.GetVertexAttributeOffset(VertexAttribute.Position);
            if (dimension != 3 || (format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16))
                throw new InvalidOperationException("Unsupported source position layout: " + source.name + " " + format);
            byte[] vertices, indices;
            using (var buffer = source.GetVertexBuffer(stream)) vertices = Read(buffer);
            using (var buffer = source.GetIndexBuffer()) indices = Read(buffer);
            var positions = new Vector3[count]; int step = format == VertexAttributeFormat.Float32 ? 4 : 2;
            if ((long)(count - 1) * stride + offset + 3 * step > vertices.Length) throw new InvalidOperationException("Short GPU vertex buffer");
            for (int i = 0; i < count; i++)
            {
                int at = i * stride + offset;
                positions[i] = new Vector3(Scalar(vertices, at, step), Scalar(vertices, at + step, step), Scalar(vertices, at + 2 * step, step));
            }
            var mesh = new Mesh { name = source.name + " private runtime contact", indexFormat = source.indexFormat, hideFlags = HideFlags.DontSave };
            mesh.vertices = positions; mesh.subMeshCount = source.subMeshCount;
            int width = source.indexFormat == IndexFormat.UInt32 ? 4 : 2;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var descriptor = source.GetSubMesh(sub);
                if (descriptor.topology != MeshTopology.Triangles) throw new InvalidOperationException("Only triangle contact meshes supported");
                if ((long)(descriptor.indexStart + descriptor.indexCount) * width > indices.Length) throw new InvalidOperationException("Short GPU index buffer");
                var triangles = new int[descriptor.indexCount];
                for (int i = 0; i < triangles.Length; i++)
                { int at = (descriptor.indexStart + i) * width; triangles[i] = width == 2 ? BitConverter.ToUInt16(indices, at) : checked((int)BitConverter.ToUInt32(indices, at)); }
                mesh.SetIndices(triangles, MeshTopology.Triangles, sub, false, descriptor.baseVertex);
            }
            mesh.bounds = source.bounds; copies.Add(source, mesh); return mesh;
        }
        static byte[] Read(GraphicsBuffer buffer)
        {
            if (buffer == null || !buffer.IsValid()) throw new InvalidOperationException("Imported GPU buffer unavailable");
            var request = AsyncGPUReadback.Request(buffer); request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("GPU buffer readback failed");
            return request.GetData<byte>().ToArray();
        }
        static float Scalar(byte[] bytes, int offset, int width)
        {
            if (width == 4) return BitConverter.ToSingle(bytes, offset);
            ushort half = BitConverter.ToUInt16(bytes, offset);
            int exponent = (half >> 10) & 31, fraction = half & 1023;
            float magnitude = exponent == 0 ? fraction * Mathf.Pow(2, -24) : exponent == 31 ?
                (fraction == 0 ? float.PositiveInfinity : float.NaN) : (1f + fraction / 1024f) * Mathf.Pow(2, exponent - 15);
            return (half & 32768) == 0 ? magnitude : -magnitude;
        }
        public static void Clear() { foreach (var mesh in copies.Values) UnityEngine.Object.Destroy(mesh); copies.Clear(); }
    }
}
