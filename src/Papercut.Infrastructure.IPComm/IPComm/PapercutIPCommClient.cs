﻿// Papercut
// 
// Copyright © 2008 - 2012 Ken Robertson
// Copyright © 2013 - 2017 Jaben Cargman
//  
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//  
// http://www.apache.org/licenses/LICENSE-2.0
//  
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License. 

namespace Papercut.Infrastructure.IPComm.IPComm
{
    using System;
    using System.Net.Sockets;
    using System.Text;

    using Papercut.Common.Domain;
    using Papercut.Common.Extensions;

    using Serilog;

    public class PapercutIPCommClient : IDisposable
    {
        public const string Localhost = "127.0.0.1";

        public const int ClientPort = 37402;

        public const int ServerPort = 37403;

        readonly ILogger _logger;

        public PapercutIPCommClient(ILogger logger)
        {
            this._logger = logger;
            this.Host = Localhost;
            this.Port = ClientPort;
        }

        public string Host { get; set; }

        public int Port { get; set; }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
        }

        private T TryConnect<T>(Func<TcpClient, T> doOperation)
        {
            IAsyncResult result = null;

            try
            {
                var client = new TcpClient();

                try
                {
                    result = client.BeginConnect(this.Host, this.Port, null, null);

                    var success = result.AsyncWaitHandle.WaitOne(100);

                    if (success)
                    {
                        return doOperation(client);
                    }
                }
                finally
                {
                    if (result != null)
                    {
                        client.EndConnect(result);
                    }

                    client.Close();
                    client.Dispose();
                }

            }
            catch (ObjectDisposedException)
            {
                // already disposed
            }
            catch (SocketException)
            {
                // no listener
            }

            return default(T);
        }

        public TEvent ExchangeEventServer<TEvent>(TEvent @event) where TEvent : IEvent
        {
            TEvent returnEvent = default(TEvent);

            TEvent DoOperation(TcpClient client)
            {
                using (var stream = client.GetStream())
                {
                    this._logger.Debug("Exchanging {@Event} with Remote", @event);

                    var isSuccessful = this.HandlePublishEvent(
                        stream,
                        @event,
                        PapercutIPCommCommandType.Exchange);

                    if (isSuccessful)
                    {
                        var response = stream.ReadString().Trim();
                        if (response == "REPLY")
                        {
                            // get exchanged event
                            returnEvent = PapercutIPCommSerializer.FromJson<TEvent>(stream.ReadString());
                        }
                    }

                    stream.Flush();

                    return returnEvent;
                }
            }

            return this.TryConnect(DoOperation);
        }

        public bool PublishEventServer<TEvent>(TEvent @event) where TEvent : IEvent
        {
            bool DoOperation(TcpClient client)
            {
                using (var stream = client.GetStream())
                {
                    this._logger.Debug("Publishing {@Event} to Remote", @event);

                    var isSuccessful = this.HandlePublishEvent(
                        stream,
                        @event,
                        PapercutIPCommCommandType.Publish);

                    stream.Flush();

                    return isSuccessful;
                }
            }

            return this.TryConnect(DoOperation);
        }

        bool HandlePublishEvent<TEvent>(
            NetworkStream stream,
            TEvent @event,
            PapercutIPCommCommandType protocolCommandType) where TEvent : IEvent
        {
            string response = stream.ReadString().Trim();

            if (response != "PAPERCUT") return false;

            this._logger.Debug("Publishing {@Event} to Remote", @event);

            var eventJson = PapercutIPCommSerializer.ToJson(@event);

            stream.WriteLine(PapercutIPCommSerializer.ToJson(
                new PapercutIPCommRequest()
                {
                    CommandType = protocolCommandType,
                    Type = @event.GetType(),
                    ByteSize = Encoding.UTF8.GetBytes(eventJson).Length
                }));

            response = stream.ReadString().Trim();
            if (response == "ACK") stream.WriteStr(eventJson, Encoding.UTF8);

            return true;
        }
    }
}