using System;

namespace Thinktecture.Relay.OnPremiseConnector.OnPremiseTarget
{
	internal interface IOnPremiseTargetConnectorFactory
	{
		IOnPremiseTargetConnector Create(Uri baseUri, TimeSpan requestTimeout, bool relayRedirects);
		IOnPremiseTargetConnector Create(Type handlerType, TimeSpan requestTimeout);
		IOnPremiseTargetConnector Create(Func<IOnPremiseInProcHandler> handlerFactory, TimeSpan requestTimeout);
		IOnPremiseTargetConnector Create<T>(TimeSpan requestTimeout) where T : IOnPremiseInProcHandler, new();
	}
}
