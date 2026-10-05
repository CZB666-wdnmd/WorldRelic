using UnityEngine;

// Field layout adapter only. Program bytes come from the user file at runtime.
namespace VRC.Udon.ProgramSources
{
    public sealed class SerializedUdonProgramAsset : ScriptableObject
    {
        public byte[] serializedProgramCompressedBytes;
        public string serializedProgramBytesString;
        public byte[] serializedSignature;
        public Object[] programUnityEngineObjects;
        public int serializationDataFormat;
    }
}
