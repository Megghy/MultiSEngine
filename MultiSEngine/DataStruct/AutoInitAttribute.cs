using System;

namespace MultiSEngine.DataStruct
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AutoInitAttribute : Attribute
    {
        public AutoInitAttribute(string preMsg = null, string postMsg = null)
        {
            PreInitMessage = preMsg;
            PostInitMessage = postMsg;
        }
        public string PreInitMessage { get; set; }
        public string PostInitMessage { get; set; }
    }
}
