﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Kayak;
using Kayak.Http;
using log4net;

namespace HttpMock
{
    public class HttpServer : IHttpServer
    {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly RequestHandlerFactory _requestHandlerFactory;
        private readonly RequestProcessor _requestProcessor;
        private readonly RequestWasCalled _requestWasCalled;
        private readonly RequestWasNotCalled _requestWasNotCalled;
        private readonly IScheduler _scheduler;
        private readonly Uri _uri;
        private IDisposable _disposableServer;
        private Thread _thread;

        public HttpServer(Uri uri)
        {
            _uri = uri;
            _scheduler = KayakScheduler.Factory.Create(new SchedulerDelegate());
            _requestProcessor = new RequestProcessor(new EndpointMatchingRule(), new RequestHandlerList());
            _requestWasCalled = new RequestWasCalled(_requestProcessor);
            _requestWasNotCalled = new RequestWasNotCalled(_requestProcessor);
            _requestHandlerFactory = new RequestHandlerFactory(_requestProcessor);
        }

        public void Start()
        {
            _thread = new Thread(StartListening);
            _thread.Start();
            if (!IsAvailable())
            {
                throw new InvalidOperationException("Kayak server not listening yet.");
            }
        }

        public bool IsAvailable()
        {
            const int timesToWait = 5;
            var attempts = 0;
            using (var tcpClient = new TcpClient())
            {
                while (attempts < timesToWait)
                {
                    try
                    {
                        tcpClient.Connect(_uri.Host, _uri.Port);
                        return tcpClient.Connected;
                    }
                    catch (SocketException)
                    {
                    }

                    Thread.Sleep(100);
                    attempts++;
                }
                return false;
            }
        }

        public void Dispose()
        {
            if (_scheduler != null)
            {
                _scheduler.Stop();
                _scheduler.Dispose();
            }
            if (_disposableServer != null)
            {
                _disposableServer.Dispose();
            }
        }

        public IRequestStub Stub(Func<RequestHandlerFactory, IRequestStub> func)
        {
            return func.Invoke(_requestHandlerFactory);
        }

        public IRequestVerify AssertWasCalled(Func<RequestWasCalled, IRequestVerify> func)
        {
            return func.Invoke(_requestWasCalled);
        }

        public IRequestVerify AssertWasNotCalled(Func<RequestWasNotCalled, IRequestVerify> func)
        {
            return func.Invoke(_requestWasNotCalled);
        }

        public IHttpServer WithNewContext()
        {
            _requestProcessor.ClearHandlers();
            return this;
        }

        public IHttpServer WithNewContext(string baseUri)
        {
            WithNewContext();
            return this;
        }

        public string WhatDoIHave()
        {
            return _requestProcessor.WhatDoIHave();
        }

        private void StartListening()
        {
            try
            {
                var ipEndPoint = new IPEndPoint(IPAddress.Any, _uri.Port);
                Exception e = null;
                _scheduler.Post(() =>
                {
                    try
                    {
                        _disposableServer = KayakServer.Factory
                            .CreateHttp(_requestProcessor, _scheduler)
                            .Listen(ipEndPoint);
                    }
                    catch (Exception ex)
                    {
                        e = ex;
                        _log.Error("Error when trying to post actions to the scheduler in StartListening", ex);
                    }
                });

                _scheduler.Start();
                Thread.Sleep(100);
                if (e != null)
                    throw e;
            }
            catch (Exception ex)
            {
                _log.Error("Error when trying to StartListening", ex);
            }
        }
    }
}