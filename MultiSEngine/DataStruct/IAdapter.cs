namespace MultiSEngine.DataStruct
{
    public interface IClientAdapter<T> where T : Core.Adapter.ClientAdapter
    {
        public T CAdapter { get; set; }
    }
    public interface IServerAdapter<T> where T : Core.Adapter.ServerAdapter
    {
        public T SAdapter { get; set; }
    }
}
