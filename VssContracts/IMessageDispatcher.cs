using System;

namespace VssContracts
{
    public enum MessageType { Error, Fatal, Info }

    public enum MessageChoice
    {
        Ok,
        OkCancel,
        OkCancelIgnore,
    }

    public enum MessageHandleResult
    {
        Ok,
        Cancel,
        Ignore
    }

    public interface IMessageDispatcher
    {
        MessageHandleResult Dispatch(MessageType messageType, string message, MessageChoice choice);
    }
}
