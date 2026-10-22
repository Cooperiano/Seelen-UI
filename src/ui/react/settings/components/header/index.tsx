import { invoke, SeelenCommand } from "@seelen-ui/lib";
import { ResourceText } from "libs/ui/react/components/ResourceText/index.tsx";
import { getCurrentWebviewWindow } from "@tauri-apps/api/webviewWindow";
import { Button, Modal } from "antd";
import React, { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { NavLink, useLocation, useNavigate, useSearchParams } from "react-router";

import { RouteExtraInfo } from "./ExtraInfo.tsx";
import { UpdateButton } from "./UpdateButton.tsx";
import { AutoSaveActions } from "./AutoSaveActions.tsx";
import cs from "./index.module.css";
import { themes as themeList, widgets as widgetList } from "../../state/resources.ts";
import { hasChanges, needRestart, restoreToLastSaved, saveSettings } from "../../state/mod.ts";
import { suiteDevelopmentBuild } from "../../state/desktopSuite.ts";
import { componentPreferencesDirty, componentPreferencesFailure, componentSavePending, discardComponentPreferences, saveComponentPreferences, trackingAccelerationChanged } from "../../state/componentPreferences.ts";

export const Header = () => {
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const dirty = hasChanges.value || componentPreferencesDirty.value;
  const [saving, setSaving] = useState(false);
  const saveBusy = useRef(false);
  const closeConfirmOpen = useRef(false);

  useEffect(() => {
    if (suiteDevelopmentBuild) return;
    const window = getCurrentWebviewWindow();
    const listener = window.onCloseRequested((event) => {
      if (componentSavePending.value || saveBusy.current) { event.preventDefault(); return; }
      if (!hasChanges.value && !componentPreferencesDirty.value) return;
      event.preventDefault();
      if (closeConfirmOpen.current) return;
      closeConfirmOpen.current = true;
      Modal.confirm({
        title: t("suite_preferences.discard_confirm"), content: t("suite_preferences.discard_description"),
        okText: t("suite_preferences.discard"), cancelText: t("cancel"), autoFocusButton: "cancel",
        onOk: async () => { discardComponentPreferences(); restoreToLastSaved(); await window.close(); },
        afterClose: () => { closeConfirmOpen.current = false; },
      });
    });
    return () => { void listener.then((unlisten) => unlisten()); };
  }, [t]);

  const SaveOrQuit = async () => {
    if (componentSavePending.value || saving) return;
    saveBusy.current = true;
    setSaving(true);
    try {
    let confirmed = false;
    if (trackingAccelerationChanged.value) {
      confirmed = await new Promise<boolean>((resolve) => Modal.confirm({
        title: t("suite_preferences.acceleration_confirm"), content: t("suite_preferences.acceleration_warning"),
        okText: t("save"), cancelText: t("cancel"), autoFocusButton: "cancel",
        onOk: () => resolve(true), onCancel: () => resolve(false),
      }));
      if (!confirmed) return;
    }
    if (componentPreferencesDirty.value && !await saveComponentPreferences(confirmed)) {
      if (componentPreferencesFailure.value) navigate(`/${componentPreferencesFailure.value}`);
      return;
    }
    if (hasChanges.value) {
      if (!await saveSettings()) {
        Modal.error({ title: t("suite_preferences.failed") });
        return;
      }
      if (needRestart.value) {
        await invoke(SeelenCommand.AppRestart);
      }
    } else if (!dirty) {
      await getCurrentWebviewWindow().close();
    }
    } finally { saveBusy.current = false; setSaving(false); }
  };

  const saveBtnLabel = needRestart.value ? t("save_and_restart") : t("save");

  let label: React.ReactNode = <span>null!?</span>;
  let parts = location.pathname === "/" ? ["home"] : location.pathname.split("/").filter(Boolean);

  if (parts[0] === "widget") {
    const widgetId = searchParams.get("id");
    const widget = widgetList.value.find((w) => w.id === widgetId);
    label = widget ? <ResourceText text={widget.metadata.displayName} /> : <span>{widgetId}</span>;
  } else if (parts[0] === "theme") {
    const themeId = searchParams.get("id");
    const theme = themeList.value.find((t) => t.id === themeId);
    label = theme ? <ResourceText text={theme.metadata.displayName} /> : <span>{themeId}</span>;
  } else {
    if (parts[0] === "wallpaper") {
      parts = ["resources", "wallpaper", "config"];
    }

    label = parts.map((part, idx) => (
      <React.Fragment key={part}>
        {idx !== parts.length - 1
          ? (
            <NavLink to={`/${parts.slice(0, idx + 1).join("/")}`} className={cs.part}>
              {t(`header.labels.${part}`)}
            </NavLink>
          )
          : (
            <span className={cs.part}>
              {t(
                part === "stats" ? "desktop_suite.stats"
                  : part === "touchpad" ? "desktop_suite.touchpad"
                  : part === "home"
                  ? "desktop_suite.title"
                  : part === "extras" && suiteDevelopmentBuild
                  ? "desktop_suite.about"
                  : `header.labels.${part}`,
              )}
            </span>
          )}
        {++idx < parts.length ? ">" : ""}
      </React.Fragment>
    ));
  }

  const ExtraInfo = RouteExtraInfo[location.pathname];

  return (
    <div className={cs.header}>
      <div className={cs.title}>
        {label}
        {ExtraInfo && <ExtraInfo />}
      </div>
      <div className={cs.actions}>
        <UpdateButton />
        {suiteDevelopmentBuild ? <AutoSaveActions /> : <>
        <Button
          style={{ minWidth: 60 }}
          type="default"
          danger
          disabled={!dirty || componentSavePending.value || saving}
          onClick={() => { discardComponentPreferences(); restoreToLastSaved(); }}
        >
          {t("cancel")}
        </Button>
        <Button
          style={{ minWidth: 60 }}
          type="primary"
          danger={!dirty}
          disabled={componentSavePending.value || saving}
          loading={componentSavePending.value || saving}
          onClick={SaveOrQuit}
        >
          {dirty ? saveBtnLabel : t("close")}
        </Button>
        </>}
      </div>
    </div>
  );
};
