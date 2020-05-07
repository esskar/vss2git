using System.Windows.Forms;
using VssContracts;

namespace Hpdi.Vss2Git
{
    public class MessageBoxDispatcher : IMessageDispatcher
    {
        public MessageHandleResult Dispatch(MessageType messageType, string message, MessageChoice choice)
        {
            var buttons = choice == MessageChoice.Ok
                ? MessageBoxButtons.OK
                : choice == MessageChoice.OkCancel
                    ? MessageBoxButtons.OKCancel
                    : MessageBoxButtons.AbortRetryIgnore;
            var icon = messageType == MessageType.Error 
                ? MessageBoxIcon.Error 
                : messageType == MessageType.Info 
                    ? MessageBoxIcon.Information 
                    : MessageBoxIcon.Stop;
            var result = MessageBox.Show(message, messageType.ToString(), buttons, icon);
            switch (result)
            {
                case DialogResult.Abort:
                case DialogResult.Cancel:
                    return MessageHandleResult.Cancel;

                case DialogResult.Retry:
                case DialogResult.OK:
                    return MessageHandleResult.Ok;
                
                case DialogResult.Ignore:
                    return MessageHandleResult.Ignore;

                default:
                    return MessageHandleResult.Cancel;
            }
        }
    }
}
