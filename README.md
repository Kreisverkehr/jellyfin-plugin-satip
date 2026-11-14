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

This plugin is still in an early development stage and should be considered experimental. Significant code and functionality changes are still likely, and breaking changes may occur between versions. It is not yet recommended for production use.

### Available Now

The plugin currently:

- discovers SAT>IP servers and available tuners on the network
- reads channel entries from the M3U playlist advertised by a compatible server
- exposes the streams to Jellyfin as SAT>IP Live TV sources
- uses a custom DVB metadata decoder for EPG parsing
- scans EPG data per multiplex instead of per individual channel, which improves the consistency of schedule collection

Channel discovery still depends on the server exposing an M3U playlist. Some SAT>IP servers do not provide one, because the playlist is optional.

### Planned

- improve EPG aggregation and reliability across repeated scans and multiple tuners
- extract more transport-stream metadata such as codecs and stream compatibility hints
- continue improving compatibility with SAT>IP servers that expose incomplete or non-standard information
- explore a manual channel scan or fallback flow for servers without M3U playlists
- implement a custom demultiplexer so a single SAT>IP stream can be shared safely among multiple channels and viewers without one client blocking the others

These items are still in progress and may evolve as the plugin matures.

### Tuner Setup Limitation

Jellyfin's tuner setup currently transfers the selected SAT>IP device into the setup form, but does not visibly show which device was selected. The selection must therefore be trusted for now. After selecting a device, you still need to click **Save** to add it to Jellyfin.

Showing a visible confirmation of the selected device before saving requires changes to Jellyfin Web; the plugin cannot change this built-in setup form on its own.

### EPG Status

EPG collection is now performed per multiplex and the custom DVB decoder is used during scanning. This reduces the issue where scans could be incomplete or stall when the schedule data is not received in the expected time window.

Still, EPG reception depends on the upstream SAT>IP stream and the server's metadata quality. Some multiplexes may still deliver incomplete or inconsistent schedule data.

## Use of AI

I use AI tools for research, code generation, and bug hunting. I personally review all AI-generated code and clean it up where necessary. My goal is to understand and be able to trace every line of code in this project.
