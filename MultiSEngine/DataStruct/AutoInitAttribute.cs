namespace MultiSEngine.DataStruct
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AutoInitAttribute : Attribute
    {
        public AutoInitAttribute(string preMsg = null, string postMsg = null, int order = 100)
        {
            PreInitMessage = preMsg;
            PostInitMessage = postMsg;
            Order = order;
        }
        public int Order { get; private set; }
        public string PreInitMessage { get; private set; }
        public string PostInitMessage { get; private set; }
    }
}
