using System;
using System.Collections.Generic;

namespace CloudSOA.Client
{
    /// <summary>
    /// Typed broker response — compatible with HPC Pack BrokerResponse&lt;T&gt;.
    /// Accessing Result when IsFault is true throws, matching HPC Pack behavior.
    /// </summary>
    public class BrokerResponse<T> where T : class
    {
        private T _result;

        /// <summary>
        /// The deserialized response. Throws if IsFault is true (HPC Pack compatible).
        /// </summary>
        public T Result
        {
            get
            {
                if (IsFault)
                    throw new InvalidOperationException(
                        "Cannot access Result of a faulted response: " + FaultMessage);
                return _result;
            }
            set { _result = value; }
        }

        public string UserData { get; set; }
        public bool IsFault { get; set; }
        public string FaultMessage { get; set; }
    }
}
