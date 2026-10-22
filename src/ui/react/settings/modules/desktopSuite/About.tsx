import { invoke, SeelenCommand } from "@seelen-ui/lib";
import { Button, Switch } from "antd";
import { useTranslation } from "react-i18next";
import { SettingsGroup, SettingsOption } from "../../components/SettingsBox/index.tsx";
import { getStreamingMode, setStreamingMode } from "../extras/application.ts";

export function SuiteInformation() {
  const { t } = useTranslation();
  return (
    <>
      <SettingsGroup>
        <SettingsOption label="Stats" description={t("desktop_suite.integration_boundary")} />
        <SettingsOption label={t("desktop_suite.upstream")} description={t("desktop_suite.upstream_version")} />
        <SettingsOption
          label={t("desktop_suite.local_development")}
          description={t("desktop_suite.development_policy")}
        />
      </SettingsGroup>
      <SettingsGroup>
        <SettingsOption
          label={t("extras.streaming_mode")}
          description={t("extras.streaming_mode_description")}
          action={<Switch value={getStreamingMode()} onChange={setStreamingMode} />}
        />
        <SettingsOption
          label={t("extras.relaunch")}
          action={<Button onClick={() => invoke(SeelenCommand.AppRestart)}>{t("open")}</Button>}
        />
        <SettingsOption
          label={t("extras.exit")}
          action={
            <Button
              danger
              onClick={() => invoke(SeelenCommand.AppExit)}
            >
              {t("quit")}
            </Button>
          }
        />
      </SettingsGroup>
    </>
  );
}
