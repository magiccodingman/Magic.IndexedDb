using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    public class JsResponse<T>
    {
        public JsResponse(T data, bool success, string message)
        {
            Data = data;
            Success = success;
            Message = message;
        }

        /// <summary>
        /// Dynamic typed response data
        /// </summary>
        public T Data { get; set; }
        /// <summary>
        /// Boolean indicator for successful API call
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// Human readable message to describe success / error conditions
        /// </summary>
        public string Message { get; set; }
    }
}
