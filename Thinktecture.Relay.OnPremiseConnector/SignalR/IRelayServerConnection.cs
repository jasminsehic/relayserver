using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Thinktecture.Relay.OnPremiseConnector.SignalR
{
	internal interface IRelayServerConnection : IDisposable
	{
		string RelayedRequestHeader { get; set; }
		Uri Uri { get; }
		TimeSpan TokenRefreshWindow { get; }
		DateTime TokenExpiry { get; }
		int RelayServerConnectionInstanceId { get; }
		DateTime LastHeartbeat { get; }
		TimeSpan HeartbeatInterval { get; }

		event EventHandler Disposing;

		void RegisterOnPremiseTarget(string key, Uri baseUri, bool relayRedirects);
		void RegisterOnPremiseTarget(string key, Type handlerType);
		void RegisterOnPremiseTarget(string key, Func<IOnPremiseInProcHandler> handlerFactory);
		void RegisterOnPremiseTarget<T>(string key) where T : IOnPremiseInProcHandler, new();
		void RemoveOnPremiseTarget(string key);
		Task ConnectAsync();
		void Disconnect();
		void Reconnect();
		Task<bool> TryRequestAuthorizationTokenAsync();

		List<string> GetOnPremiseTargetKeys();
		Task<HttpResponseMessage> GetToRelay(string relativeUrl, Action<HttpRequestHeaders> setHeaders, CancellationToken cancellationToken);
		Task<HttpResponseMessage> PostToRelay(string relativeUrl, Action<HttpRequestHeaders> setHeaders, HttpContent content, CancellationToken cancellationToken);
	}
}
