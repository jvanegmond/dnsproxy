using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DNS.Client;
using DNS.Client.RequestResolver;
using DNS.Protocol;
using DNS.Protocol.Utils;
using DNS.Server;

namespace DnsProxy
{
    public class DnsServer : IDisposable
    {
        private bool _run = true;
        private bool _disposed;
        private const int _defaultPort = 53;
        private const int _udpTimeout = 2000;
        private UdpClient _udp;
        private readonly IRequestResolver _resolver;

        public event EventHandler<RequestedEventArgs> Requested;

        public event EventHandler<RespondedEventArgs> Responded;

        public event EventHandler<EventArgs> Listening;

        public event EventHandler<ErroredEventArgs> Errored;

        public DnsServer(MasterFile masterFile, IPEndPoint endServer)
          : this(new FallbackRequestResolver(masterFile, new UdpRequestResolver(endServer)))
        {
        }

        public DnsServer(MasterFile masterFile, IPAddress endServer, int port = _defaultPort)
          : this(masterFile, new IPEndPoint(endServer, port))
        {
        }

        public DnsServer(MasterFile masterFile, string endServer, int port = _defaultPort)
          : this(masterFile, IPAddress.Parse(endServer), port)
        {
        }

        public DnsServer(IPEndPoint endServer)
          : this(new UdpRequestResolver(endServer))
        {
        }

        public DnsServer(IPAddress endServer, int port = _defaultPort)
          : this(new IPEndPoint(endServer, port))
        {
        }

        public DnsServer(string endServer, int port = _defaultPort)
          : this(IPAddress.Parse(endServer), port)
        {
        }

        public DnsServer(IRequestResolver resolver)
        {
            _resolver = resolver;
        }

        public Task Listen(int port = _defaultPort, IPAddress ip = null)
        {
            return Listen(new IPEndPoint(ip ?? IPAddress.Any, port));
        }

        public async Task Listen(IPEndPoint endpoint)
        {
            await Task.Yield();
            var tcs = new TaskCompletionSource<object>();
            if (_run)
            {
                try
                {
                    _udp = new UdpClient(endpoint);
                }
                catch (SocketException ex)
                {
                    OnError(ex);
                    return;
                }
            }

            void ReceiveCallback(IAsyncResult result)
            {
                try
                {
                    var remoteEp = new IPEndPoint(0L, 0);
                    HandleRequest(_udp.EndReceive(result, ref remoteEp), remoteEp);
                }
                catch (ObjectDisposedException)
                {
                    _run = false;
                }
                catch (SocketException ex)
                {
                    OnError(ex);
                }

                do
                {
                    try
                    {
                        if (_run)
                        {
                            _udp.BeginReceive(ReceiveCallback, null);
                        }
                        else
                        {
                            tcs.SetResult(null);
                        }

                        break;
                    }
                    catch (SocketException ex)
                    {
                        OnError(ex);
                    }
                } while (true);
            }

            do
            {
                try
                {
                    _udp.BeginReceive(ReceiveCallback, null);

                    break;
                }
                catch (SocketException ex)
                {
                    OnError(ex);
                }
            } while (true);

            OnEvent(Listening, EventArgs.Empty);
            await tcs.Task;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void OnEvent<T>(EventHandler<T> handler, T args)
        {
            handler?.Invoke(this, args);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
                _run = false;
                _udp?.Dispose();
            }
        }

        private void OnError(Exception e)
        {
            OnEvent(Errored, new ErroredEventArgs(e));
        }

        private async void HandleRequest(byte[] data, IPEndPoint remote)
        {
            Request request = null;
            try
            {
                request = Request.FromArray(data);
                OnEvent(Requested, new RequestedEventArgs(request, data, remote));
                var response = await _resolver.Resolve(request);
                OnEvent(Responded, new RespondedEventArgs(request, response, data, remote));
                await _udp.SendAsync(response.ToArray(), response.Size, remote).WithCancellationTimeout(_udpTimeout);
            }
            catch (SocketException ex)
            {
                OnError(ex);
            }
            catch (ArgumentException ex)
            {
                OnError(ex);
            }
            catch (IndexOutOfRangeException ex)
            {
                OnError(ex);
            }
            catch (OperationCanceledException ex)
            {
                OnError(ex);
            }
            catch (IOException ex)
            {
                OnError(ex);
            }
            catch (ObjectDisposedException ex)
            {
                OnError(ex);
            }
            catch (ResponseException ex)
            {
                var response = ex.Response ?? Response.FromRequest(request);
                try
                {
                    await _udp.SendAsync(response.ToArray(), response.Size, remote).WithCancellationTimeout(2000);
                }
                catch (SocketException)
                {
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    OnError(ex);
                }
            }
        }

        public class RequestedEventArgs : EventArgs
        {
            public RequestedEventArgs(IRequest request, byte[] data, IPEndPoint remote)
            {
                Request = request;
                Data = data;
                Remote = remote;
            }

            public IRequest Request { get; }

            public byte[] Data { get; }

            public IPEndPoint Remote { get; }
        }

        public class RespondedEventArgs : EventArgs
        {
            public RespondedEventArgs(IRequest request, IResponse response, byte[] data, IPEndPoint remote)
            {
                Request = request;
                Response = response;
                Data = data;
                Remote = remote;
            }

            public IRequest Request { get; }

            public IResponse Response { get; }

            public byte[] Data { get; }

            public IPEndPoint Remote { get; }
        }

        public class ErroredEventArgs : EventArgs
        {
            public ErroredEventArgs(Exception e)
            {
                Exception = e;
            }

            public Exception Exception { get; }
        }

        private class FallbackRequestResolver : IRequestResolver
        {
            private readonly IRequestResolver[] _resolvers;

            public FallbackRequestResolver(params IRequestResolver[] resolvers)
            {
                _resolvers = resolvers;
            }

            public async Task<IResponse> Resolve(IRequest request)
            {
                IResponse response = null;
                var requestResolverArray = _resolvers;
                foreach (var resolver in requestResolverArray)
                {
                    response = await resolver.Resolve(request);
                    if (response.AnswerRecords.Count <= 0)
                    {
                    }
                    else
                    {
                        break;
                    }
                }
                return response;
            }
        }
    }
}
