using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Windows;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Notification;
using Microsoft.Phone.Shell;
using Newtonsoft.Json;

namespace WPCordovaClassLib.Cordova.Commands
{
    public class PushPlugin : BaseCommand
    {
        private const string InvalidRegistrationError = "Unable to open a channel with the specified name. The most probable cause is that you have already registered a channel with a different name. Call unregister(old-channel-name) or uninstall and redeploy your application.";
        private const string MissingChannelError = "Couldn't find a channel with the specified name.";
        private const string JSONError = "JSON Exception.";
        private Options pushOptions;

        public void init(string args)
        {
            register(args, true);
        }
        
        public void register(string options, bool retry)
        {
             if (!TryDeserializeOptions(options, out this.pushOptions))
             {
                 SendError(JSONError);
                 return;
             }

             HttpNotificationChannel pushChannel = HttpNotificationChannel.Find(this.pushOptions.WP8.ChannelName);
             
             if (pushChannel == null)
             {
                 pushChannel = new HttpNotificationChannel(this.pushOptions.WP8.ChannelName);

                 try
                 {
                     pushChannel.Open();
                 }
                 catch (InvalidOperationException)
                 {
                     SendError(InvalidRegistrationError);
                     return;
                 }

                 pushChannel.BindToShellToast();
                 pushChannel.BindToShellTile();
             }

             SubscribePushChannelEvents(pushChannel);

             if (pushChannel.ChannelUri == null && retry) 
             {   
            	 register(options, false);
             } 
             else 
             {
                 RegisterResult result = new RegisterResult
                 {
                     ChannelName = this.pushOptions.WP8.ChannelName,
                     Uri = pushChannel.ChannelUri == null ? string.Empty : pushChannel.ChannelUri.ToString()
                 };

                SendEvent(JsonConvert.SerializeObject(result));
             }
        }

        public void unregister(string options)
        {
            Options unregisterOptions;
            
            if (!TryDeserializeOptions(options, out unregisterOptions))
            {
                SendError(JSONError);
                return;
            }
            
            HttpNotificationChannel pushChannel = HttpNotificationChannel.Find(unregisterOptions.WP8.ChannelName);
            
            if (pushChannel != null)
            {
                pushChannel.UnbindToShellTile();
                pushChannel.UnbindToShellToast();
                pushChannel.Close();
                pushChannel.Dispose();

                SendEvent("Channel " + unregisterOptions.WP8.ChannelName + " is closed!");
            }
            else
            {
                SendError(MissingChannelError);
            }
        }

        public void showToastNotification(string options)
        {
            ShellToast toast;
            
            if (!TryDeserializeOptions(options, out toast))
            {
                SendError(JSONError);
                return;
            }

            Deployment.Current.Dispatcher.BeginInvoke(toast.Show);
        }

        void PushChannel_ChannelUriUpdated(object sender, NotificationChannelUriEventArgs e)
        {
            // return uri to js
            RegisterResult result = new RegisterResult
            {
                ChannelName = this.pushOptions.WP8.ChannelName,
                Uri = e.ChannelUri.ToString()
            };

            SendEvent(JsonConvert.SerializeObject(result));
        }

        void PushChannel_ErrorOccurred(object sender, NotificationChannelErrorEventArgs e)
        {
            // call error handler and return uri
            RegisterError err = new RegisterError
            {
                Code = e.ErrorCode.ToString(),
                Message = e.Message
            };
            
            SendError(JsonConvert.SerializeObject(err));
        }

        void PushChannel_ShellToastNotificationReceived(object sender, NotificationEventArgs e)
        {
        	Dictionary<string, object> content = new Dictionary<string, object>();
        	
        	PushNotification toast = new PushNotification
            {
                Type = "toast",
                Content = content
            };
            
            foreach (var item in e.Collection)
            {            	
                content.Add(item.Key, item.Value);
            }

            SendEvent(JsonConvert.SerializeObject(toast));
        }

        void PushChannel_HttpNotificationReceived(object sender, HttpNotificationEventArgs e)
        {            
            object content = null;
            
            using (StreamReader reader = new StreamReader(e.Notification.Body))
            {
            	content = JsonConvert.DeserializeObject<object>(reader.ReadToEnd());
            }

            PushNotification raw = new PushNotification
            {
                Type = "raw",
                Content = content
            };         
            
            SendEvent(JsonConvert.SerializeObject(raw));
        }
                
        private void SendEvent(String json)
        {
            PluginResult pluginResult = new PluginResult(PluginResult.Status.OK, json);
            pluginResult.KeepCallback = true;
            this.DispatchCommandResult(pluginResult);
        }

        private void SendError(String message)
        {
            PluginResult pluginResult = new PluginResult(PluginResult.Status.ERROR, message);
            pluginResult.KeepCallback = true;
            this.DispatchCommandResult(pluginResult);
        }

        static bool TryDeserializeOptions<T>(string options, out T result) where T : class
        {
            result = null;
            
            try
            {
                String[] args = JsonConvert.DeserializeObject<string[]>(options);
                result = JsonConvert.DeserializeObject<T>(args[0]);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TryCast<T>(object obj, out T result) where T : class
        {
            result = obj as T;
            return result != null;
        }
    
        void SubscribePushChannelEvents(HttpNotificationChannel channel)
        {
            channel.ChannelUriUpdated += new EventHandler<NotificationChannelUriEventArgs>(PushChannel_ChannelUriUpdated);
            channel.ErrorOccurred += new EventHandler<NotificationChannelErrorEventArgs>(PushChannel_ErrorOccurred);
            channel.ShellToastNotificationReceived += new EventHandler<NotificationEventArgs>(PushChannel_ShellToastNotificationReceived);
            channel.HttpNotificationReceived += new EventHandler<HttpNotificationEventArgs>(PushChannel_HttpNotificationReceived);
        }

        [DataContract]
        public class Options
        {
        	[DataMember(Name = "wp8", IsRequired = true)]
        	public WP8Options WP8 { get; set; }
        }
        
        [DataContract]
        public class WP8Options
        {        	
            [DataMember(Name = "channelName", IsRequired = true)]
            public string ChannelName { get; set; }
        }

        [DataContract]
        public class RegisterResult
        {
            [DataMember(Name = "registrationId", IsRequired = true)]
            public string Uri { get; set; }

            [DataMember(Name = "channel", IsRequired = true)]
            public string ChannelName { get; set; }
        }

        [DataContract]
        public class PushNotification
        {
            [DataMember(Name = "content", IsRequired = true)]
            public object Content { get; set; }

            [DataMember(Name = "type", IsRequired = true)]
            public string Type { get; set; }
        }

        [DataContract]
        public class RegisterError
        {
            [DataMember(Name = "code", IsRequired = true)]
            public string Code { get; set; }

            [DataMember(Name = "message", IsRequired = true)]
            public string Message { get; set; }
        }
    }
}
