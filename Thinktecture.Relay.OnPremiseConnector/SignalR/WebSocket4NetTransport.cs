using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using Microsoft.AspNet.SignalR.Client.Infrastructure;
using Microsoft.AspNet.SignalR.Client.Transports;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace Thinktecture.Relay.OnPremiseConnector.SignalR
{
	public sealed class WebSocket4NetTransport : ClientTransportBase
	{
		private IConnection _connection;
		private string _connectionData;
		private CancellationToken _disconnectToken;
		private CancellationTokenSource _webSocketTokenSource;
		private WebSocket _webSocket4Net;
		private int _disposed;

		public TimeSpan ReconnectDelay { get; set; }

		public WebSocket4NetTransport() : this(new DefaultHttpClient())
		{
		}

		public WebSocket4NetTransport(IHttpClient client) : base(client, "webSockets")
		{
			_disconnectToken = CancellationToken.None;
			ReconnectDelay = TimeSpan.FromSeconds(2.0);
		}

		~WebSocket4NetTransport()
		{
			Dispose(false);
		}

		protected override void OnStart(IConnection connection, string connectionData, CancellationToken disconnectToken)
		{
			_connection = connection;
			_connectionData = connectionData;
			_disconnectToken = disconnectToken;

			var connectUrl = UrlBuilder.BuildConnect(connection, Name, connectionData);

			try
			{
				PerformConnect(connectUrl);
			}
			catch (Exception ex)
			{
				TransportFailed(ex);
			}
		}

		protected override void OnStartFailed()
		{
			Dispose();
		}

		public override Task Send(IConnection connection, string data, string connectionData)
		{
			if (_webSocket4Net.State == WebSocketState.Open)
			{
				_webSocket4Net.Send(data);
			}

			var ex = new InvalidOperationException("Socket closed");
			connection.OnError(ex);

			throw ex;
		}

		public override void LostConnection(IConnection connection)
		{
			_connection.Trace(TraceLevels.Events, "WS: LostConnection");
			_webSocketTokenSource?.Cancel();
		}

		public override bool SupportsKeepAlive => true;

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (Interlocked.Exchange(ref _disposed, 1) == 1)
				{
					base.Dispose(true);
					return;
				}

				_webSocketTokenSource?.Cancel();

				if (_webSocket4Net != null)
				{
					DisposeWebSocket4Net();
				}

				_webSocketTokenSource?.Dispose();
			}

			base.Dispose(disposing);
		}

		private void DisposeWebSocket4Net()
		{
			_webSocket4Net.Error -= WebSocketOnError;
			_webSocket4Net.Opened -= WebSocketOnOpened;
			_webSocket4Net.Closed -= WebSocketOnClosed;
			_webSocket4Net.MessageReceived -= WebSocketOnMessageReceived;

			_webSocket4Net.Dispose();
			_webSocket4Net = null;
		}

		private void PerformConnect(string url)
		{
			if (_webSocket4Net != null)
			{
				DisposeWebSocket4Net();
			}

			_webSocketTokenSource = new CancellationTokenSource();
			_webSocketTokenSource.Token.Register(WebSocketTokenSourceCanceled);
			CancellationTokenSource.CreateLinkedTokenSource(_webSocketTokenSource.Token, _disconnectToken);

			// Add the header from the connection to the socket connection
			var headers = _connection.Headers.ToList();

			// SignalR uses https, websocket4net uses wss
			url = url.Replace("http://", "ws://").Replace("https://", "wss://");

			_webSocket4Net = new WebSocket(url, customHeaderItems: headers);

			_webSocket4Net.Error += WebSocketOnError;
			_webSocket4Net.Opened += WebSocketOnOpened;
			_webSocket4Net.Closed += WebSocketOnClosed;
			_webSocket4Net.MessageReceived += WebSocketOnMessageReceived;

			_webSocket4Net.Open();
		}

		private async Task DoReconnect()
		{
			string reconnectUrl = UrlBuilder.BuildReconnect(_connection, Name, _connectionData);

			while (TransportHelper.VerifyLastActive(_connection))
			{
				if (_connection.EnsureReconnecting())
				{
					try
					{
						PerformConnect(reconnectUrl);
						break;
					}
					catch (OperationCanceledException)
					{
						break;
					}
					catch (Exception ex)
					{
						_connection.OnError(ex);
					}
					await Task.Delay(ReconnectDelay, CancellationToken.None);
				}
				else
				{
					break;
				}
			}
		}

		private void WebSocketOnOpened(object sender, EventArgs e)
		{
			_connection.Trace(TraceLevels.Events, "WS: OnOpen()");

			if (!_connection.ChangeState(ConnectionState.Reconnecting, ConnectionState.Connected))
			{
				return;
			}

			_connection.OnReconnected();
		}

		private async void WebSocketOnClosed(object sender, EventArgs e)
		{
			_connection.Trace(TraceLevels.Events, "WS: OnClose()");

			if (_disconnectToken.IsCancellationRequested || AbortHandler.TryCompleteAbort())
			{
				return;
			}

			await DoReconnect();
		}

		private void WebSocketOnError(object sender, ErrorEventArgs e)
		{
			var exception = e.Exception;
			_connection.OnError(exception);
		}

		private void WebSocketOnMessageReceived(object sender, MessageReceivedEventArgs e)
		{
			var message = e.Message;

			_connection.Trace(TraceLevels.Messages, "WS: OnMessage({0})", (object)message);
			ProcessResponse(_connection, message);
		}

		private void WebSocketTokenSourceCanceled()
		{
			if (_webSocketTokenSource.IsCancellationRequested)
			{
				if (_webSocket4Net.State != WebSocketState.Closed)
				{
					_webSocket4Net.Close(1000, "");
				}
			}
		}
	}
}
