const SatIpConfigurationPage = {
    pluginUniqueId: '3ebdc5ac-57aa-4ed0-aedf-122f99a6976f'
};

export default function (view) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;

        ApiClient.getPluginConfiguration(SatIpConfigurationPage.pluginUniqueId).then(function (config) {
            page.querySelector('#txtAdditionalPlaylistUris').value = (config.AdditionalPlaylistUris || []).join('\n');
            page.querySelector('#txtEpgScanDurationSeconds').value = config.EpgScanDurationSeconds ?? 30;
            page.querySelector('#txtUdpPortRangeStart').value = config.UdpPortRangeStart ?? 50000;
            page.querySelector('#txtUdpPortRangeEnd').value = config.UdpPortRangeEnd ?? 50101;
            page.querySelector('#chkIgnoreServerPlaylist').checked = config.IgnoreServerPlaylist || false;
            page.querySelector('#chkReplacePlaylistHostWithTunerHost').checked = config.ReplacePlaylistHostWithTunerHost !== false;
            Dashboard.hideLoadingMsg();
        });
    });

    view.querySelector('.SatIpConfigurationForm').addEventListener('submit', function (event) {
        event.preventDefault();
        const form = this;
        const startPort = Number(form.querySelector('#txtUdpPortRangeStart').value);
        const endPort = Number(form.querySelector('#txtUdpPortRangeEnd').value);
        if (!Number.isInteger(startPort)
            || startPort < 49152
            || startPort > 65534
            || startPort % 2 !== 0
            || !Number.isInteger(endPort)
            || endPort < 49153
            || endPort > 65535
            || endPort % 2 !== 1
            || startPort >= endPort) {
            Dashboard.alert('Ports must be within 49152-65535. The range must start with an even RTP port and end with an odd RTCP port, and include at least one complete pair.');
            return false;
        }

        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(SatIpConfigurationPage.pluginUniqueId).then(function (config) {
            config.AdditionalPlaylistUris = form.querySelector('#txtAdditionalPlaylistUris').value
                .split(/\r?\n/)
                .map(function (uri) { return uri.trim(); })
                .filter(function (uri) { return uri.length > 0; });
            config.EpgScanDurationSeconds = Number(form.querySelector('#txtEpgScanDurationSeconds').value);
            config.UdpPortRangeStart = startPort;
            config.UdpPortRangeEnd = endPort;
            config.IgnoreServerPlaylist = form.querySelector('#chkIgnoreServerPlaylist').checked;
            config.ReplacePlaylistHostWithTunerHost = form.querySelector('#chkReplacePlaylistHostWithTunerHost').checked;

            return ApiClient.updatePluginConfiguration(SatIpConfigurationPage.pluginUniqueId, config);
        }).then(Dashboard.processPluginConfigurationUpdateResult);

        return false;
    });
}