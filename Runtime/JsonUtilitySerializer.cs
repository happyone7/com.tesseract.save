using UnityEngine;

namespace Tesseract.Save
{
    /// <summary>
    /// Default serializer using Unity's built-in JsonUtility.
    /// For complex types (dictionaries, polymorphism), use NewtonsoftSerializer instead.
    /// </summary>
    public class JsonUtilitySerializer : ISaveSerializer
    {
        public string Serialize<T>(T data)
        {
            return JsonUtility.ToJson(data, true);
        }

        public T Deserialize<T>(string json)
        {
            return JsonUtility.FromJson<T>(json);
        }
    }
}
