namespace Tesseract.Save
{
    /// <summary>
    /// Interface for save data serialization. Implement to use custom serializers.
    /// Default implementation uses Unity's JsonUtility.
    /// </summary>
    public interface ISaveSerializer
    {
        string Serialize<T>(T data);
        T Deserialize<T>(string json);
    }
}
