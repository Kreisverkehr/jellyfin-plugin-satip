# Jellyfin SAT>IP Plugin

![Development stage: early](https://img.shields.io/badge/development-early-orange)
![Production use: not ready](https://img.shields.io/badge/production-not_ready-red)

This project is a Jellyfin plugin that connects SAT>IP servers to Jellyfin Live TV. It is intended to let Jellyfin discover SAT>IP tuners and use their streams directly, without requiring an additional TV backend such as TVHeadend or NextPVR.

## Why I Started This Project

I started this project because I was looking for a straightforward way to use my FRITZ!Box as a Live TV source in Jellyfin. I wanted to connect it directly, rather than set up another service in between, such as TVHeadend or NextPVR.

I chose SAT>IP because it is a standard that may also help other people with a similar setup, beyond my own FRITZ!Box use case.

## Use of AI

I use AI tools for research, code generation, and bug hunting. I personally review all AI-generated code and clean it up where necessary. My goal is to understand and be able to trace every line of code in this project.

## Project Status & Roadmap

This plugin is at a very early stage of development. Significant code and functionality changes are likely, and breaking changes should be expected. It is not ready for production use and should not be deployed in a production environment.

### Available Now

The plugin discovers SAT>IP servers on the network and reads channel entries from the M3U playlist advertised by a server. The streams are then made available to Jellyfin as SAT>IP Live TV sources.

Channel discovery currently depends on the server providing an M3U playlist. Some SAT>IP servers may not provide one, since the playlist is optional.

### Tuner Setup Limitation

Jellyfin's tuner setup currently transfers the selected SAT>IP device into the setup form, but does not visibly show which device was selected. The selection must therefore be trusted for now. After selecting a device, you still need to click **Save** to add it to Jellyfin.

Showing a visible confirmation of the selected device before saving requires changes to Jellyfin Web; the plugin cannot change this built-in setup form on its own.

### Planned

- Make the plugin available for installation through a Jellyfin plugin repository.
- Retrieve and integrate EPG data from the SAT>IP server.
- Explore a manual channel scan that does not depend on an M3U playlist.

These features are planned and are not available yet.
