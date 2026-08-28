using System.Text;
using Xunit.Sdk;

namespace Tftp.Net.Tests.Serializers;

public class OptionCollectionSerializer : XunitSerializer<IEnumerable<KeyValuePair<string, string>>>
{
    public override IEnumerable<KeyValuePair<string, string>> Deserialize(Type type, string serializedValue)
    {
        throw new NotImplementedException();
    }

    public override string Serialize(IEnumerable<KeyValuePair<string, string>> value) => 
		SerializeOptions(value);

	internal static string SerializeOptions(IEnumerable<KeyValuePair<string, string>> options)
	{
		var stringBuilder = new StringBuilder();

		foreach (var option in options)
		{
			stringBuilder.Append($"{option.Key} = {option.Value}, ");
		}

		return stringBuilder.ToString().TrimEnd(' ', ',');
	}
}
