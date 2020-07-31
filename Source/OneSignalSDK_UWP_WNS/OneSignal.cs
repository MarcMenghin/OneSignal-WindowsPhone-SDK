using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Diagnostics;
using Windows.Networking.PushNotifications;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.UI.Core;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding;

namespace OneSignalSDK_UWP_WNS
{

    public class OneSignal
    {
        public const string VERSION = "010101";

        private const string BASE_URL = "https://onesignal.com/api/v1/";
        private static string mAppId;
        private static string mPlayerId, mChannelUri;
        private static long lastPingTime;
        private static ApplicationDataContainer settings = ApplicationData.Current.LocalSettings;
        private static bool initDone = false;
        private static bool foreground = true;

        public delegate void NotificationReceived(string message, IDictionary<string, string> additionalData, bool isActive);
        public static NotificationReceived notificationDelegate = null;

        public delegate void IdsAvailable(string playerID, string pushToken);
        public static IdsAvailable idsAvailableDelegate = null;

        public delegate void TagsReceived(IDictionary<string, string> tags);
        public static TagsReceived tagsReceivedDelegate = null;

        private static IDisposable fallBackOneSignalSession;

        private static bool sessionCallInProgress, sessionCallDone, subscriptionChangeInProgress;

        private static ExternalInit externalInit = null;

        public static void Init(string appId, LaunchActivatedEventArgs launchArgs, NotificationReceived inNotificationDelegate = null)
        {
            Init(appId, launchArgs.Arguments, inNotificationDelegate, null);
        }

        public static async void Init(string appId, string launchArgs, NotificationReceived inNotificationDelegate = null, ExternalInit inExternalInit = null)
        {
            await Log("Started Init!");
            mAppId = appId;
            externalInit = inExternalInit;

            if (inNotificationDelegate != null)
                notificationDelegate = inNotificationDelegate;

            mPlayerId = (string)settings.Values["OneSignalPlayerId"];
            mChannelUri = (string)settings.Values["OneSignalChannelUri"];

            await Log("Check NotificationOpened");
            checkForNotificationOpened(launchArgs);

            if (initDone)
                return;

            fallBackOneSignalSession = new Timer(async o => { await SendSession(null); }, null, 20000, Timeout.Infinite);

            CoreWindow.GetForCurrentThread().VisibilityChanged += OneSignal_VisibilityChanged_Window_Current;

            lastPingTime = DateTime.Now.Ticks;

            await Log("Starting Getting PushUri");
            // async
            GetPushUri();

            //register background task
            //await OneSignalBackgroundTask.RegisterIfNotAlready();

            initDone = true;
            await Log("Init Done!!");
        }

        private static async void OneSignal_VisibilityChanged_Window_Current(object sender, VisibilityChangedEventArgs args)
        {
            await Log("OneSignal_VisibilityChanged_Window_Current");
            foreground = args.Visible;

            if (foreground)
                lastPingTime = DateTime.Now.Ticks;
            else
            {
                var time_elapsed = (long)((((DateTime.Now.Ticks) - lastPingTime) / 10000000) + 0.5);
                lastPingTime = DateTime.Now.Ticks;

                if (time_elapsed < 0 || time_elapsed > 604800)
                    return;

                var unSentActiveTime = GetSavedActiveTime();
                var totalTimeActive = unSentActiveTime + time_elapsed;

                if (totalTimeActive < 30)
                {
                    settings.Values["OneSignalActiveTime"] = totalTimeActive;
                    return;
                }

                await SendPing(totalTimeActive);
                settings.Values["OneSignalActiveTime"] = (long)0;
            }

            if (externalInit != null && foreground)
                checkForNotificationOpened(externalInit.GetAppArguments());
        }

        private static void checkForNotificationOpened(string args)
        {
            if (!args.Equals(""))
            {
                var json = JObject.Parse(args);
                if (json["custom"] != null)
                    NotificationOpened("", args, true);
            }
        }

        private static async void GetPushUri()
        {
            var channel = await PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync();

            channel.PushNotificationReceived += channel_PushNotificationReceived;

            await Log("GetPushUri call SendSession()");
            try
            {
                await SendSession(channel.Uri);
            }
            catch (Exception ex)
            {
                await Log("ERROR GetPushUri:" + ex.ToString());
            }
        }

        static async void channel_PushNotificationReceived(PushNotificationChannel sender, PushNotificationReceivedEventArgs args)
        {
            await Log("PushNotificationRecieved");

            if (foreground
               && args.NotificationType == PushNotificationType.Toast
               && args.ToastNotification.Content.FirstChild.Attributes.GetNamedItem("launch") != null)
            {

                try
                {
                    string lauchJson = (string)args.ToastNotification.Content.FirstChild.Attributes.GetNamedItem("launch").NodeValue;
                    var json = JObject.Parse(lauchJson);

                    if (json["custom"] != null)
                    {
                        var bindingNode = args.ToastNotification.Content.SelectSingleNode("/toast/visual/binding");
                        string text1 = bindingNode.SelectSingleNode("text[@id='2']").InnerText;

                        args.ToastNotification.SuppressPopup = true;
                        args.Cancel = true;
                        NotificationOpened(text1, lauchJson, false);
                    }
                }
                catch (Exception e)
                {
                }
            }
        }

        private static long GetSavedActiveTime()
        {
            if (settings.Values.ContainsKey("OneSignalActiveTime"))
                return (long)(settings.Values["OneSignalActiveTime"]);
            return 0;
        }

        private static async Task SendPing(long activeTime)
        {
            if (mPlayerId == null)
                return;

            await Log("Sending Ping");

            var jsonObject = JObject.FromObject(new
            {
                state = "ping",
                active_time = activeTime
            });

            var client = GetHttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(BASE_URL + "players/" + mPlayerId + "/on_focus"));
            request.Content = new HttpStringContent(jsonObject.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

            await client.SendRequestAsync(request);
        }

        private static LoggingSession session = null;
        private static LoggingChannel defaultLoggingChannel = null;
        private static StorageFolder logFolder = null;

        public static async Task Log(string message)
        {
            if (session == null)
            {
                logFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("log", CreationCollisionOption.OpenIfExists);
                await logFolder.CreateFileAsync("SearchForMeToFindMeFile", CreationCollisionOption.ReplaceExisting);

                session = new LoggingSession("onesignal_tapget");
                defaultLoggingChannel = new LoggingChannel("OneSignal", null);
                session.AddLoggingChannel(defaultLoggingChannel);
                defaultLoggingChannel.LogMessage("Starting new log!");
            }

            const string prefix = "OneSignal: ";
            Debug.WriteLine(prefix + message, "OneSignal");

            defaultLoggingChannel.LogMessage(prefix + message, LoggingLevel.Information);

            try
            {
                await session.SaveToFileAsync(logFolder, "onesignal-tapget-log.log.etl");
            }
            catch
            {

            }
        }

        private static readonly object SendSessionLock = new object();

        private static async Task SendSession(string currentChannelUri)
        {
            await Log("Send Session called");

            lock (SendSessionLock)
            {
                if (sessionCallInProgress || sessionCallDone)
                    return;

                sessionCallInProgress = true;
            }

            await Log("Send Session started");

            string adId = Windows.System.UserProfile.AdvertisingManager.AdvertisingId;

            if (currentChannelUri != null && mChannelUri != currentChannelUri)
            {
                mChannelUri = currentChannelUri;
                settings.Values["OneSignalChannelUri"] = mChannelUri;
            }

            var deviceInformation = new EasClientDeviceInformation();

            PackageVersion pv = Package.Current.Id.Version;
            String appVersion = pv.Major + "." + pv.Minor + "." + pv.Revision + "." + pv.Build;

            JObject jsonObject = JObject.FromObject(new
            {
                device_type = 6,
                app_id = mAppId,
                identifier = mChannelUri,
                ad_id = adId,
                device_model = deviceInformation.SystemProductName,
                game_version = appVersion,
                language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToString(),
                timezone = TimeZoneInfo.Local.BaseUtcOffset.TotalSeconds.ToString(),
                sdk = VERSION
            });

            string urlString = "players";
            if (mPlayerId != null)
                urlString += "/" + mPlayerId + "/on_session";
            else
                jsonObject.Add(new JProperty("sdk_type", externalInit != null ? externalInit.sdkType : "native"));

            await Log("Session object created");

            var client = GetHttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(BASE_URL + urlString));
            request.Content = new HttpStringContent(jsonObject.ToString(), UnicodeEncoding.Utf8, "application/json");

            await Log("Sending session information.");

            var responseTask = await client.SendRequestAsync(request);

            await Log("Did send Device Information!");
            fallBackOneSignalSession.Dispose();


            if (responseTask.IsSuccessStatusCode)
            {
                sessionCallDone = true;
                string content = await responseTask.Content.ReadAsStringAsync();
                var jObject = JObject.Parse(content);
                string newId = (string)jObject["id"];
                if (newId != null)
                {
                    mPlayerId = newId;
                    settings.Values["OneSignalPlayerId"] = newId;
                    idsAvailableDelegate?.Invoke(mPlayerId, mChannelUri);
                }
            }

            sessionCallInProgress = false;
        }

        private static async void NotificationOpened(string message, string jsonParams, bool openedFromNotification)
        {
            await Log("NotificationOpened");
            JObject jObject = JObject.Parse(jsonParams);

            JObject jsonObject = JObject.FromObject(new
            {
                app_id = mAppId,
                player_id = mPlayerId,
                opened = true
            });

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, new Uri(BASE_URL + "notifications/" + (string)jObject["custom"]["i"]));
            request.Content = new HttpStringContent(jsonObject.ToString(), UnicodeEncoding.Utf8, "application/json");
            GetHttpClient().SendRequestAsync(request);

            if (openedFromNotification && jObject["custom"]["u"] != null)
            {
                var uri = new Uri((string)jObject["custom"]["u"], UriKind.Absolute);
                Windows.System.Launcher.LaunchUriAsync(uri);
            }

            if (notificationDelegate != null)
            {
                var additionalDataJToken = jObject["custom"]["a"];
                IDictionary<string, string> additionalData = null;

                if (additionalDataJToken != null)
                    additionalData = additionalDataJToken.ToObject<Dictionary<string, string>>();

                notificationDelegate(message, additionalData, initDone);
            }
        }

        private static HttpClient GetHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders
                  .Accept
                  .Add(new HttpMediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }


        public static async Task SetSubscriptionAsync(bool status)
        {
            if (mPlayerId == null || mAppId == null || subscriptionChangeInProgress)
            {
                return;
            }

            subscriptionChangeInProgress = true;

            JObject jsonObject = JObject.FromObject(new
            {
                notification_types = status ? "1" : "-2",
            });
            string urlString = "players/" + mPlayerId;

            var client = GetHttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, new Uri(BASE_URL + urlString));
            request.Content = new HttpStringContent(jsonObject.ToString(), UnicodeEncoding.Utf8, "application/json");

            var responseTask = await client.SendRequestAsync(request);

            subscriptionChangeInProgress = false;
        }

        public static void SendTag(string key, string value)
        {
            var dictionary = new Dictionary<string, object>();
            dictionary.Add(key, value);
            SendTags((IDictionary<string, object>)dictionary);
        }

        public static void SendTags(IDictionary<string, string> keyValues)
        {
            var newDict = new Dictionary<string, object>();
            foreach (var item in keyValues)
                newDict.Add(item.Key, item.Value.ToString());

            SendTags(newDict);
        }

        public static void SendTags(IDictionary<string, int> keyValues)
        {
            var newDict = new Dictionary<string, object>();
            foreach (var item in keyValues)
                newDict.Add(item.Key, item.Value.ToString());

            SendTags(newDict);
        }

        public static void SendTags(IDictionary<string, object> keyValues)
        {
            if (mPlayerId == null)
                return;

            JObject jsonObject = JObject.FromObject(new
            {
                tags = keyValues
            });

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, new Uri(BASE_URL + "players/" + mPlayerId));
            request.Content = new HttpStringContent(jsonObject.ToString(), UnicodeEncoding.Utf8, "application/json");
            GetHttpClient().SendRequestAsync(request);
        }

        public static void DeleteTags(IList<string> tags)
        {
            if (mPlayerId == null)
                return;

            var dictionary = new Dictionary<string, string>();
            foreach (string key in tags)
                dictionary.Add(key, "");

            JObject jsonObject = JObject.FromObject(new
            {
                tags = dictionary
            });

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, new Uri(BASE_URL + "players/" + mPlayerId));
            request.Content = new HttpStringContent(jsonObject.ToString(), UnicodeEncoding.Utf8, "application/json");
            GetHttpClient().SendRequestAsync(request);
        }

        public static void DeleteTag(string tag)
        {
            DeleteTags(new List<string>() { tag });
        }

        public static void SendPurchase(double amount)
        {
            SendPurchase((decimal)amount);
        }

        public static void SendPurchase(decimal amount)
        {
            if (mPlayerId == null)
                return;

            JObject jsonObject = JObject.FromObject(new
            {
                amount = amount
            });

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, new Uri(BASE_URL + "players/" + mPlayerId + "/on_purchase"));
            request.Content = new HttpStringContent(jsonObject.ToString(), UnicodeEncoding.Utf8, "application/json");
            GetHttpClient().SendRequestAsync(request);
        }

        public static void GetIdsAvailable()
        {
            if (idsAvailableDelegate == null)
                throw new ArgumentNullException("Assign idsAvailableDelegate before calling or call GetIdsAvailable(IdsAvailable)");

            if (mPlayerId != null)
                idsAvailableDelegate(mPlayerId, mChannelUri);
        }

        public static void GetIdsAvailable(IdsAvailable inIdsAvailableDelegate)
        {
            idsAvailableDelegate = inIdsAvailableDelegate;

            if (mPlayerId != null)
                idsAvailableDelegate(mPlayerId, mChannelUri);
        }

        public static void GetTags()
        {
            if (mPlayerId == null)
                return;

            if (tagsReceivedDelegate == null)
                throw new ArgumentNullException("Assign tagsReceivedDelegate before calling or call GetTags(TagsReceived)");

            SendGetTagsMessage();
        }

        public static void GetTags(TagsReceived inTagsReceivedDelegate)
        {
            if (mPlayerId == null)
                return;

            tagsReceivedDelegate = inTagsReceivedDelegate;

            SendGetTagsMessage();
        }

        private static async void SendGetTagsMessage()
        {
            var responseTask = await GetHttpClient().GetAsync(new Uri(BASE_URL + "players/" + mPlayerId));



            if (responseTask.IsSuccessStatusCode)
            {
                string content = await responseTask.Content.ReadAsStringAsync();
                tagsReceivedDelegate(JObject.Parse(content)["tags"].ToObject<Dictionary<string, string>>());
            }


        }
    }
}