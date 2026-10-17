# Jellyfin SAT>IP Plugin

![Development stage: early](https://img.shields.io/badge/development-early-orange)
![Production use: not ready](https://img.shields.io/badge/production-not_ready-red)

This project is a Jellyfin plugin that connects SAT>IP servers to Jellyfin Live TV. It is intended to let Jellyfin discover SAT>IP tuners and use their streams directly, without requiring an additional TV backend such as TVHeadend or NextPVR.

## Installation

1. Open the Jellyfin Dashboard.
2. Navigate to **Plugins → Repositories**.
3. Add a new repository with the following details:

   - **Repository name:** Kreisverkehr Jellyfin Plugins
   - **Repository URL:** `https://raw.githubusercontent.com/kreisverkehr/jellyfin-plugins/main/manifest.json`

4. Save the repository configuration.
5. Open the plugin catalog, select the "SAT>IP Live TV Tuner" plugin, and install it.
6. Restart Jellyfin if required.

## Why I Started This Project

I started this project because I was looking for a straightforward way to use my FRITZ!Box as a Live TV source in Jellyfin. I wanted to connect it directly, rather than set up another service in between, such as TVHeadend or NextPVR.

I chose SAT>IP because it is a standard that may also help other people with a similar setup, beyond my own FRITZ!Box use case.

## Project Status & Roadmap

This plugin is at a very early stage of development. Significant code and functionality changes are likely, and breaking changes should be expected. It is not ready for production use and should not be deployed in a production environment.

### Available Now

The plugin discovers SAT>IP servers on the network and reads channel entries from the M3U playlist advertised by a server. The streams are then made available to Jellyfin as SAT>IP Live TV sources.

Channel discovery currently depends on the server providing an M3U playlist. Some SAT>IP servers may not provide one, since the playlist is optional.

### Tuner Setup Limitation

Jellyfin's tuner setup currently transfers the selected SAT>IP device into the setup form, but does not visibly show which device was selected. The selection must therefore be trusted for now. After selecting a device, you still need to click **Save** to add it to Jellyfin.

Showing a visible confirmation of the selected device before saving requires changes to Jellyfin Web; the plugin cannot change this built-in setup form on its own.

### EPG Data Limitation

EPG data is currently collected in a 60-second scan for each channel. DVB schedule sections are transmitted in repeating cycles, and a cycle may take longer than the scan, so the program guide can be incomplete. Results are not yet accumulated or shared between channels on the same multiplex.

### Planned

- Make the plugin available for installation through a Jellyfin plugin repository.
- Collect EIT schedule sections across scans and cache/share the results per multiplex, ideally until the expected sections have been received.
- Extract channel metadata such as codecs from the transport stream and expose it to Jellyfin so it can identify compatible streams earlier and avoid unnecessary transcoding where possible. This may require changing the channel ID format.
- Explore a manual channel scan that does not depend on an M3U playlist.

These features are planned and are not available yet.

## Use of AI

I use AI tools for research, code generation, and bug hunting. I personally review all AI-generated code and clean it up where necessary. My goal is to understand and be able to trace every line of code in this project.
