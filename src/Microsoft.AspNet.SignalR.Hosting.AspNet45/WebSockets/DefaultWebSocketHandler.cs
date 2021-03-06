﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.AspNet.SignalR.WebSockets
{
    internal class DefaultWebSocketHandler : WebSocketHandler, IWebSocket
    {
        private bool _raiseEvent = true;

        public override void OnClose(bool clean)
        {
            if (!_raiseEvent)
            {
                return;
            }

            Action<bool> onClose = ((IWebSocket)this).OnClose;
            if (onClose != null)
            {
                onClose(clean);
            }
        }

        public override void OnError()
        {
            Action<Exception> onError = ((IWebSocket)this).OnError;
            if (onError != null)
            {
                // REVIEW: What error do we throw here?
                onError(Error);
            }
        }

        public override void OnMessage(string message)
        {
            Action<string> onMessage = ((IWebSocket)this).OnMessage;
            if (onMessage != null)
            {
                onMessage(message);
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
        }

        Action<string> IWebSocket.OnMessage
        {
            get;
            set;
        }

        Action<bool> IWebSocket.OnClose
        {
            get;
            set;
        }

        Action<Exception> IWebSocket.OnError
        {
            get;
            set;
        }

        Task IWebSocket.Send(string value)
        {
            return Send(value);
        }

        public void CleanClose()
        {
            _raiseEvent = false;
            Close();
        }
    }
}
