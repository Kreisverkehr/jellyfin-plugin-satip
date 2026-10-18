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
            page.querySelector('#chkIgnoreServerPlaylist').checked = config.IgnoreServerPlaylist || false;
            page.querySelector('#chkReplacePlaylistHostWithTunerHost').checked = config.ReplacePlaylistHostWithTunerHost !== false;
            Dashboard.hideLoadingMsg();
        });
    });

    view.querySelector('.SatIpConfigurationForm').addEventListener('submit', function (event) {
        event.preventDefault();
        Dashboard.showLoadingMsg();
        const form = this;

        ApiClient.getPluginConfiguration(SatIpConfigurationPage.pluginUniqueId).then(function (config) {
            config.AdditionalPlaylistUris = form.querySelector('#txtAdditionalPlaylistUris').value
                .split(/\r?\n/)
                .map(function (uri) { return uri.trim(); })
                .filter(function (uri) { return uri.length > 0; });
            config.EpgScanDurationSeconds = Number(form.querySelector('#txtEpgScanDurationSeconds').value);
            config.IgnoreServerPlaylist = form.querySelector('#chkIgnoreServerPlaylist').checked;
            config.ReplacePlaylistHostWithTunerHost = form.querySelector('#chkReplacePlaylistHostWithTunerHost').checked;

            return ApiClient.updatePluginConfiguration(SatIpConfigurationPage.pluginUniqueId, config);
        }).then(Dashboard.processPluginConfigurationUpdateResult);

        return false;
    });
}