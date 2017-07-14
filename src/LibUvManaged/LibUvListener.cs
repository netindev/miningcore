﻿using System;
using System.Net;
using Autofac;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration.Extensions;

namespace LibUvManaged
{
    public class LibUvListener
    {
        public LibUvListener(IComponentContext ctx)
        {
            this.logger = ctx.Resolve<ILogger<LibUvListener>>();
            this.ctx = ctx;
            this.tracer = new LibuvTrace(logger);
        }

        internal readonly ILibuvTrace tracer;
        internal UvLoopHandle loop;
        private UvAsyncHandle stopEvent;
        internal LibuvFunctions uv;
        private readonly ILogger<LibUvListener> logger;
        private readonly IComponentContext ctx;

        public string EndpointId { get; set; }

        public void Start(IPEndPoint endPoint, Action<ILibUvConnection> connectionHandlerFactory)
        {
            try
            {
                loop = new UvLoopHandle(tracer);

                uv = new LibuvFunctions();
                stopEvent = new UvAsyncHandle(tracer);

                loop.Init(uv);

                stopEvent.Init(loop, () =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    loop.Stop();
                }, null);

                var socket = new UvTcpHandle(tracer);
                socket.Init(loop, null);
                socket.Bind(endPoint);

                var listenState = Tuple.Create(this, connectionHandlerFactory);
                socket.Listen(LibuvConstants.ListenBacklog, OnNewConnection, listenState);

                logger.Info(() => $"Listening on {endPoint}");

                loop.Run();

                logger.Info(() => $"Stopped listening on {endPoint}");

                // close handles
                uv.walk(loop, (handle, state) => uv.close(handle, null), IntPtr.Zero);

                // invoke handle-close-callbacks
                loop.Run();

                // done
                loop.Close();
            }

            catch (Exception ex)
            {
                tracer.LogError(ex.ToString());
                throw;
            }
        }

        public void Stop()
        {
            stopEvent?.Send();
        }

        private static void OnNewConnection(UvStreamHandle server, int status, UvException ex, object _state)
        {
            var state = (Tuple<LibUvListener, Action<LibUvConnection>>)_state;
            var self = state.Item1;

            if (status >= 0)
            {
                var con = new LibUvConnection(self.ctx, self, (UvTcpHandle) server, state.Item2);
                con.Init();
            }

            else
                self.tracer.ConnectionError("-1", ex);
        }
    }
}