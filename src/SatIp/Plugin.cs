using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private const string ID = "3ebdc5ac-57aa-4ed0-aedf-122f99a6976f";
    private readonly ILogger<Plugin> _logger;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger) : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        MigrateConfiguration();
        Instance = this;
        if (!UriParser.IsKnownScheme("rtsp"))
            UriParser.Register(new GenericUriParser(GenericUriParserOptions.Default), "rtsp", 554);
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    public override string Name => "SAT>IP Live TV Tuner";

    public override string Description => "A SAT>IP server plugin for Jellyfin.";

    public override string ConfigurationFileName => "Kreisverkehr.Jellyfin.Plugin.SatIp.xml";

    public override Guid Id => new(ID);

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = "satip",
            EmbeddedResourcePath = "SatIp.Web.settings.html"
        },
        new PluginPageInfo
        {
            Name = "satipjs",
            EmbeddedResourcePath = "SatIp.Web.settings.js"
        }
    ];

    public static PluginConfiguration GetConfiguration() => Instance?.Configuration ?? new();
    
    private void MigrateConfiguration()
    {
        List<string> oldConfigFiles = ["SatIp.xml", "Kreisverkehr.SatIp.xml"];

        if(File.Exists(ConfigurationFilePath))
            return;

        foreach(var oldConfig in oldConfigFiles
            .Select(f => Path.Combine(ApplicationPaths.PluginConfigurationsPath, f))
            .Where(f => File.Exists(f)))
        {
            if(!File.Exists(ConfigurationFilePath))
            {
                _logger.LogInformation("Migrating configuration file from {oldConfig} to {newConfig}", oldConfig, ConfigurationFilePath);
                File.Move(oldConfig, ConfigurationFilePath);
            } else
            {
                _logger.LogWarning("Deleted leftover old configuration {oldConfig}", oldConfig);
                File.Delete(oldConfig);
            }
        }
    }
}