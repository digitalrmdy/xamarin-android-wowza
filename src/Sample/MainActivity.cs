using System;
using Android.App;
using Android.Widget;
using Android.OS;
using Com.Wowza.Gocoder.Sdk.Api;
using Com.Wowza.Gocoder.Sdk.Api.Configuration;
using Com.Wowza.Gocoder.Sdk.Api.Player;
using Com.Wowza.Gocoder.Sdk.Api.Status;

namespace Sample
{
    [Activity(Label = "Wowza Sample", MainLauncher = true, HardwareAccelerated = true)]
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

	        WowzaGoCoder.Init(this, "GOSK-5E45-010F-E3CB-EF7E-3513");
	        
	        SetContentView(Resource.Layout.Main);

	        var playerView = FindViewById<WOWZPlayerView>(Resource.Id.stream_player);
	        var playButton = FindViewById<Button>(Resource.Id.playButton);

	        playButton.Click += (sender, args) =>
	        {
		        var playerConfig = new WOWZPlayerConfig
		        {
			        HostAddress = "edge.cdn.wowza.com",
			        PortNumber = 1935,
			        ApplicationName = "live",
			        StreamName = "0P0p2VThFWktwdzZFL0N4OXB0ZDl581b",
			        AudioEnabled = true,
			        VideoEnabled = false
		        };
		        var error = playerConfig.ValidateForPlayback();
		        if (error != null)
		        {
			        throw new Exception("Config error: " + error.ErrorDescription);
		        }
	        
		        var wowzaCallback = new WowzaCallback();

		        playerView.Play(playerConfig, wowzaCallback);
	        };
        }
    }

	class WowzaCallback : Java.Lang.Object, IWOWZStatusCallback
	{
		public void OnWZError(WOWZStatus p0)
		{
			Console.WriteLine("Wowza error: " + p0.LastError.ErrorCode);
		}

		public void OnWZStatus(WOWZStatus p0)
		{
			Console.WriteLine("Wowza status changed: " + p0.State.ToString());
		}
	}
}

