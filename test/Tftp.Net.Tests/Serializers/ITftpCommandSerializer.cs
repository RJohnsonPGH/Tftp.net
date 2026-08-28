using System.Text;
using Tftp.Net.Commands;
using Xunit.Sdk;

namespace Tftp.Net.Tests.Serializers;

public class ITftpCommandSerializer : XunitSerializer<ICommand>
{
    public override ICommand Deserialize(Type type, string serializedValue)
    {
        throw new NotImplementedException();
    }

    public override string Serialize(ICommand value)
    {
        return value switch
        {
            ReadRequest readRequest => $"Read Request, Filename = {readRequest.Filename}, Mode = {readRequest.Mode}, {OptionCollectionSerializer.SerializeOptions(readRequest.Options)}",
            WriteRequest writeRequest => $"Write Request, Filename = {writeRequest.Filename}, Mode = {writeRequest.Mode}, {OptionCollectionSerializer.SerializeOptions(writeRequest.Options)}",
			Data data => $"BlockNumber = {data.BlockNumber}, DataBytes = {data.DataBytes.Length}",
            Acknowledgement acknowledgement => $"BlockNumber = {acknowledgement.BlockNumber}",
            Error error => $"ErrorCode = {error.ErrorCode}, Message = {error.Message}",
            OptionAcknowledgement optionAcknowledgement => OptionCollectionSerializer.SerializeOptions(optionAcknowledgement.Options),
            _ => throw new ArgumentException($"Command of type '{value.GetType().FullName}' is not supported by ITftpCommandSerializer.", nameof(value)),
        };
    }
}
