﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;

namespace Microsoft.AspNet.SignalR.Hosting.AspNet
{
    public class AspNetResponse : IResponse
    {
        private delegate void RemoveHeaderDel(HttpWorkerRequest workerRequest);

        private const string IIS7WorkerRequestTypeName = "System.Web.Hosting.IIS7WorkerRequest";
        private static readonly Lazy<RemoveHeaderDel> IIS7RemoveHeader = new Lazy<RemoveHeaderDel>(GetRemoveHeaderDelegate);
        private readonly HttpContextBase _context;
        private bool _bufferingDisabled;

        public AspNetResponse(HttpContextBase context)
        {
            _context = context;

            DisableResponseBuffering();
        }

        public bool IsClientConnected
        {
            get
            {
#if NET45
                try
                {
                    // Return true for websocket requests since connectivity is handled by SignalR's transport
                    if (_context.IsWebSocketRequest)
                    {
                        return true;
                    }
                }
                catch
                {
                    // This fails if the request isn't a websocket request or if we're not in integrated mode
                }
#endif
                try
                {
                    return _context.Response.IsClientConnected;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("SignalR AspNet host error checking for connected clients: {0}", ex.Message);
                    // This happens on cassini (built in webserver in VS)
                    return false;
                }
            }
        }

        public string ContentType
        {
            get
            {
                return _context.Response.ContentType;
            }
            set
            {
                _context.Response.ContentType = value;
            }
        }

        private void DisableResponseBuffering()
        {
            if (_bufferingDisabled)
            {
                return;
            }

            // This forces the IIS compression module to leave this response alone.
            // If we don't do this, it will buffer the response to suit its own compression
            // logic, resulting in partial messages being sent to the client.
            RemoveAcceptEncoding();

            _context.Response.CacheControl = "no-cache";
            _context.Response.AddHeader("Connection", "keep-alive");

            _bufferingDisabled = true;
        }

        private void RemoveAcceptEncoding()
        {
            try
            {
                var workerRequest = (HttpWorkerRequest)_context.GetService(typeof(HttpWorkerRequest));
                if (IsIIS7WorkerRequest(workerRequest))
                {
                    // Optimized code path for IIS7, accessing Headers causes all headers to be read
                    IIS7RemoveHeader.Value.Invoke(workerRequest);
                }
                else
                {
                    try
                    {
                        _context.Request.Headers.Remove("Accept-Encoding");
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // Happens on cassini
                    }
                }
            }
            catch (NotImplementedException)
            {
            }
        }

        public void Write(ArraySegment<byte> data)
        {
            try
            {
                if (_context.Response.IsClientConnected)
                {
                    _context.Response.OutputStream.Write(data.Array, data.Offset, data.Count);
                }
            }
            catch
            {
            }
        }

        public Task FlushAsync()
        {
            return _context.Response.FlushAsync();
        }

        public Task EndAsync()
        {
            return TaskAsyncHelper.Empty;
        }

        private static bool IsIIS7WorkerRequest(HttpWorkerRequest workerRequest)
        {
            return workerRequest != null && workerRequest.GetType().FullName == IIS7WorkerRequestTypeName;
        }

        private static RemoveHeaderDel GetRemoveHeaderDelegate()
        {
            var iis7wrType = typeof(HttpContext).Assembly.GetType(IIS7WorkerRequestTypeName);
            var methodInfo = iis7wrType.GetMethod("SetKnownRequestHeader", BindingFlags.NonPublic | BindingFlags.Instance);

            var wrParamExpr = Expression.Parameter(typeof(HttpWorkerRequest));
            var iis7wrParamExpr = Expression.Convert(wrParamExpr, iis7wrType);
            var callExpr = Expression.Call(iis7wrParamExpr, methodInfo, Expression.Constant(HttpWorkerRequest.HeaderAcceptEncoding), Expression.Constant(null, typeof(string)), Expression.Constant(false));
            return Expression.Lambda<RemoveHeaderDel>(callExpr, wrParamExpr).Compile();
        }
    }
}
