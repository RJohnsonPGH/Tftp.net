using Tftp.Net.Commands.Properties;
using Xunit.Sdk;

namespace Tftp.Net.Tests.Serializers;

public class OptionSetSerializer : XunitSerializer<OptionSet>
{
    public override OptionSet Deserialize(Type type, string serializedValue)
    {
        throw new NotImplementedException();
    }

    public override string Serialize(OptionSet value)
    {
        return $"Timeout = {value.Timeout}, BlockSize = {value.BlockSize}, TransferSize = {value.TransferSize}";
	}
}
