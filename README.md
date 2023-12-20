# HDHomeRun Proxy Service

This service functions as a proxy for an HDHomeRun device and fixes the aspect ratio on certain MPEG streams.

## Background

Some over-the-air stations broadcast their digital subchannels with the incorrect aspect ratio in the MPEG sequence header, causing the video to be displayed incorrectly by HDHomeRun devices and other players like Plex.

This issue has been discussed on various forums with no resolution:
* https://www.reddit.com/r/hdhomerun/comments/15oha6v/override_incorrect_aspect_ratio_on_ota_channels/
* https://www.reddit.com/r/PleX/comments/15ohc3w/override_incorrect_aspect_ratio_on_ota_channels/
* https://forum.silicondust.com/forum/viewtopic.php?p=393781
* https://forums.plex.tv/t/override-aspect-ratio-on-roku-app-for-plex/850185

The original user on the SiliconDust forum (the post no longer exists) had suggested that altering the bits in the MPEG header might be a way to fix the problem.

## Status

At this time, neither vendor has updated their video player to support changing the aspect ratio.

This service successfully serves as a proxy for the channel streams coming from the HDHR device. It simply modifies the 2 bits in the sequence header that specify the aspect ratio, so players like Plex and VLC can now point to the proxy address and play or record the streams in the correct format.

> *The Plex live TV and DVR feature can detect and use the proxy service as its source if the IP address or hostname of the machine hosting this service is manually entered.*

To avoid conflicts on the network, the original device ID is altered by one bit, which also results in a different checksum digit, so the last two digits of the device ID for the proxy service will differ from the original. This altered device ID is reflected in the proxy's web interface.

> **While the proxied web interface is available for testing, viewing the channel lineup, and device discovery, users should not attempt to make changes or upgrade the firmware when accessing the device via the proxy service. Doing so is untested and could cause issues on your device.**

An unsuccessful attempt was made to also proxy the discovery and control streams on the device so that the aspect ratio could be fixed on the native HDHomeRun video player as well. While two devices were visible on the network, the player detected that a firmware upgrade was necessary and could not handle the fake device. This could possibly be addressed in the future.

## Configuration

By default, the proxy service will attempt to listen on the same ports that the HDHR devices use, with port 80 for the web service and port 5004 for the video streams. It assumes the HDHR device uses the same ports, and that the device is accessible using the "hdhomerun.local" hostname.

```json
{
  "Settings": {
    "DeviceHostName": "hdhomerun.local",
    "DeviceCapturePort": "5004",
    "DeviceWebPort": "80",
    "ServiceHostName": "",
    "ServiceEndpoint": "0.0.0.0",
    "ServiceEndpointIPv6": "",
    "ServiceCapturePort": "5004",
    "ServiceWebPort": "80",
    "ProxyAllChannels": false,
    "Channels": {
      "2.2": "4:3",
      "2.3": "16:9",
      "2.4": "2.21:1",
      "2.5": "1:1"
    }
  }
}
```

In order to proxy specific channels, each channel number must be added to the "Channels" section (as shown above) with the desired aspect ratio to override in the MPEG stream. Only the values shown above are supported by [the MPEG format](http://dvdnav.mplayerhq.hu/dvdinfo/mpeghdrs.html).

> By default, only the channels specified will be proxied, with all others being sent directly to the HDHR device. If all channels should pass through the proxy service, even without changing the aspect ratio, then the "ProxyAllChannels" value may be set to true.

All of the other settings above are optional and reflect the defaults for the service if they are not specified. However, if there are address or port conflicts on your network, the service properties can be overridden.

## Requirements

This code requires the [.NET 8 runtime](https://dotnet.microsoft.com/en-us/download) as well as a compatible HDHomeRun device. At the initial release, this has only been tested on the HDHomeRun FLEX 4K (HDFX-4K) with the following firmwares:
* 20231214
* 20231020

## Installation

Download the [latest version](https://github.com/jonathanduke/fix-hdhr-aspect/releases/tag/latest) that matches your platform. Unzip it in the location of your choice and then edit the [appsettings.json](#configuration) configuration file.

On Windows, you can create a Windows service using the [sc.exe](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-create) command if you have Administrator permission:

```console
sc.exe create FixHdhrAspect binPath="C:\Path\To\FixHdhrAspect.exe" DisplayName="HDHomeRun Proxy Service"
```

Similarly, the service can be started, stopped, deleted, etc. with the same command:
```console
sc.exe start FixHdhrAspect
sc.exe stop FixHdhrAspect
sc.exe delete FixHdhrAspect
```

More detailed instructions can be found [here](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service?pivots=dotnet-7-0#create-the-windows-service).

In theory, this service should work on other operating systems as well, but that is currently untested.