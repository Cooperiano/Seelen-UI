import { invoke, SeelenCommand } from "@seelen-ui/lib";
import { getCurrentWebviewWindow } from "@tauri-apps/api/webviewWindow";
import { Button, Modal } from "antd";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { needRestart } from "../../state/mod.ts";
import { autoSaveStatus, settingsAutoSave, startSettingsAutoSave } from "../../state/settingsAutoSave.ts";
import cs from "./index.module.css";

export function AutoSaveActions() {
  const { t } = useTranslation();
  const closing = useRef(false);
  const allowClose = useRef(false);
  const [busy, setBusy] = useState(false);
  async function close() {
    if (closing.current) return;
    closing.current = true;
    setBusy(true);
    try {
      if (await settingsAutoSave.flush()) {
        allowClose.current = true;
        await getCurrentWebviewWindow().close();
      }
    } finally { closing.current = false; setBusy(false); }
  }
  useEffect(() => {
    startSettingsAutoSave();
    const listener = getCurrentWebviewWindow().onCloseRequested((event) => {
      if (allowClose.current) return;
      event.preventDefault();
      void close();
    });
    return () => { void listener.then((unlisten) => unlisten()); };
  }, []);
  const status = autoSaveStatus.value;
  return <>
    <span className={cs.saveStatus} data-error={status === "error"} role="status" aria-live="polite">
      {t(`suite_autosave.${status}`)}
    </span>
    {status === "error" && <Button onClick={() => settingsAutoSave.flush()}>{t("suite_autosave.retry")}</Button>}
    {needRestart.value && <Button disabled={busy || status === "saving"} onClick={() => Modal.confirm({
      title: t("suite_autosave.restart_title"), content: t("suite_autosave.restart_description"),
      okText: t("suite_autosave.restart"), cancelText: t("cancel"), autoFocusButton: "cancel",
      onOk: async () => { if (await settingsAutoSave.flush()) await invoke(SeelenCommand.AppRestart); },
    })}>{t("suite_autosave.restart")}</Button>}
    <Button disabled={busy} loading={busy} onClick={close}>{t("close")}</Button>
  </>;
}
