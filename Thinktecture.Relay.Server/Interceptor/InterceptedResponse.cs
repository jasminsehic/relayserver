using System.Collections.Generic;
using System.Linq;
using Thinktecture.Relay.Server.OnPremise;

namespace Thinktecture.Relay.Server.Interceptor
{
	internal class InterceptedResponse : OnPremiseConnectorResponse, IInterceptedResponse
	{
		public InterceptedResponse(IOnPremiseConnectorResponse other)
		{
			RequestId = other.RequestId;
			OriginId = other.OriginId;
			RequestStarted = other.RequestStarted;
			RequestFinished = other.RequestFinished;
			StatusCode = other.StatusCode;
			HttpHeaders = other.HttpHeaders;
			Body = other.Body; // this is because of legacy on-premise connectors (v1)
			Stream = other.Stream;
			ContentLength = other.ContentLength;
		}

		public Dictionary<string, string> CloneHttpHeaders()
		{
			return HttpHeaders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		}
	}
}
