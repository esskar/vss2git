using System;
using VssContracts;

namespace Hpdi.Vss2Git
{
    public class ConsoleLoggerMessageDispatcher : IMessageDispatcher
    {
        private readonly Logger logger;
        private readonly bool ignoreErrors;

        public ConsoleLoggerMessageDispatcher(Logger logger, bool ignoreErrors)
        {
            this.logger = logger;
            this.ignoreErrors = ignoreErrors;
        }

        public MessageHandleResult Dispatch(MessageType messageType, string message, MessageChoice choice)
        {
            var fullMessage = $"{messageType}: {message}";
            logger.WriteLine(fullMessage);
            Console.WriteLine(fullMessage);

            switch (choice)
            {
                case MessageChoice.Ok:
                    return MessageHandleResult.Ok;

                case MessageChoice.OkCancel:
                    return ignoreErrors 
                        ? MessageHandleResult.Ok : MessageHandleResult.Cancel;

                case MessageChoice.OkCancelIgnore:
                    return ignoreErrors
                        ? MessageHandleResult.Ignore
                        : MessageHandleResult.Cancel;
            }

            return MessageHandleResult.Cancel;
        }
    }
}
