using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneSignalSDK_UWP_WNS;

namespace OneSignalSDK_UWP_WNS_WRTC
{

    internal class ExternalInitCordovaInternal : ExternalInit {
      public string sdkType { get { return "cordova"; } }

      public static void Init(string appId, string launchArgs, OneSignal.NotificationReceived inNotificationDelegate = null) {
         OneSignal.Init(appId, launchArgs, inNotificationDelegate, new ExternalInitCordovaInternal());
      }

      // Not used, OneSignalPushProxy.js handles this.
      public string GetAppArguments() {
         return "";
      }
   }

   public sealed class WinJSBridge {
      public event EventHandler<NotificationOpenedEventArgs> notificationOpened;
      public event EventHandler<GetTagsEventArgs> gettagsevent;
      public event EventHandler<GetIdsEventArgs> idsavailableevent;

      public void init(string appId, string launchArgs) {
         ExternalInitCordovaInternal.Init(appId, launchArgs, (inMessage, inAdditionalData, inIsActive) => {

            // dispatcher
            CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
               if (notificationOpened != null) {
                  EventHandler<NotificationOpenedEventArgs> temp = notificationOpened;
                  temp(this, new NotificationOpenedEventArgs() {
                     message = inMessage,
                     additionalData = JsonConvert.SerializeObject(inAdditionalData),
                     isActive = inIsActive
                  });
               }
            });
         });
      }

      public void sendtags(string tags) {
         OneSignal.SendTags(JObject.Parse(tags).ToObject<Dictionary<string, object>>());
      }

      public void gettags() {
         OneSignal.GetTags((tags) => {
            CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
               gettagsevent(this, new GetTagsEventArgs() { tags = JsonConvert.SerializeObject(tags) });
            });
         });
      }

      public void getids() {
         OneSignal.GetIdsAvailable((userId, pushToken) => {
            CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
               idsavailableevent(this, new GetIdsEventArgs() { userId = userId, pushToken = pushToken });
            });
         });
      }

      public void deletetags(string tags) {
         OneSignal.DeleteTags(JArray.Parse(tags).ToObject<List<string>>());
      }
   }

   public sealed class NotificationOpenedEventArgs {
      public string message { get; set; }
      public string additionalData { get; set; }
      public bool isActive { get; set; }
   }

   public sealed class GetTagsEventArgs {
      public string tags { get; set; }
   }

   public sealed class GetIdsEventArgs {
      public string userId { get; set; }
      public string pushToken { get; set; }
   }
}
