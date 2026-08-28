using Spectre.Console.Cli;
using System;

namespace Tftp.Net.Cli.Internal;

public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
	public object? Resolve(Type? type)
	{
		if (type is null)
		{
			return null;
		}

		return provider.GetService(type);
	}

	public void Dispose()
	{
		if (provider is IDisposable disposable)
		{
			disposable.Dispose();
		}
	}
}
