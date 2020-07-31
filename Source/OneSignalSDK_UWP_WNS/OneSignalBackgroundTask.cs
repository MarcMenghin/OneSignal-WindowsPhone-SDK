using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Networking.PushNotifications;
using Windows.UI.Notifications;
using Microsoft.Toolkit.Uwp.Notifications;

namespace OneSignalSDK_UWP_WNS
{
    public sealed class OneSignalBackgroundTask : IBackgroundTask
    {
        private static bool taskRegistered = false;
        private static string BackgroundTaskName = "OneSignal Tapget Task";

        private static bool IsRegistered()
        {
            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == BackgroundTaskName)
                {
                    taskRegistered = true;
                    break;
                }
            }

            return taskRegistered;
        }

        public static async Task RegisterIfNotAlready()
        {
            //make sure background tasks are allowed by the user
            await BackgroundExecutionManager.RequestAccessAsync();

            if (IsRegistered())
            {
                await OneSignal.Log("Background Task already Registered!");
                return; //already registered
            }

            await OneSignal.Log("Registering new Background Task.");

            var builder = new BackgroundTaskBuilder();

            builder.Name = BackgroundTaskName;
            builder.TaskEntryPoint = nameof(OneSignalBackgroundTask);

            BackgroundTaskRegistration task = builder.Register();

            await OneSignal.Log("Background Task Registration done!");
        }

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            await OneSignal.Log("Background Task started!");

            var content = new ToastContent()
            {
                Launch = "T",
                Visual = new ToastVisual()
                {
                    BindingGeneric = new ToastBindingGeneric()
                    {
                        Children =
                        {
                            new AdaptiveText()
                            {
                                Text = "Test Notification"
                            },

                            new AdaptiveText()
                            {
                                Text = "Some Content"
                            }
                        },

                        AppLogoOverride = new ToastGenericAppLogo()
                        {
                            Source = "images/Square44x44Logo.png",
                            HintCrop = ToastGenericAppLogoCrop.Circle
                        }
                    }
                },
            };
            var toast = new ToastNotification(content.GetXml());
            toast.ExpirationTime = DateTime.Now.AddDays(1);
            toast.Group = "tapget";
            ToastNotificationManager.CreateToastNotifier().Show(toast);

            await OneSignal.Log("Background Task ended!");
        }
    }
}
