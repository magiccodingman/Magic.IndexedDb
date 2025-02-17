namespace Magic.IndexedDb.Models
{

    [Serializable]
    public class MagicException : Exception
    {
        public MagicException(string message, Exception? inner = null) : base(message, inner) { }
    }
}