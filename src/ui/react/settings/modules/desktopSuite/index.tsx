import { invoke, SeelenCommand } from "@seelen-ui/lib";
import { Alert, Button, Flex, Popconfirm, Switch } from "antd";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { SettingsGroup, SettingsOption } from "../../components/SettingsBox/index.tsx";
import { autostart } from "../../state/system.ts";

interface ComponentState { id: string; enabled: boolean; available: boolean; running: boolean; error: string | null }
interface SuiteState { components: ComponentState[]; autostart: boolean; taskbar?: { enabled: boolean; active: boolean } }

export function DesktopSuite() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [available, setAvailable] = useState<string[] | null>(null);
  const [pending, setPending] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [suite, setSuite] = useState<SuiteState | null>(null);

  async function refresh() {
    const next = await invoke(SeelenCommand.GetDesktopSuiteState) as unknown as SuiteState;
    autostart.value = next.autostart;
    setSuite(next);
  }

  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try {
        const next = await invoke(SeelenCommand.GetDesktopSuiteState) as unknown as SuiteState;
        if (active) {
          autostart.value = next.autostart;
          setSuite(next);
        }
      } catch { if (active) setFailed(true); }
      if (active) timer = setTimeout(poll, 3000);
    }
    poll();
    return () => { active = false; clearTimeout(timer); };
  }, [loadAttempt]);

  async function change(module: string, enabled: boolean) {
    if (pending) return;
    setPending(module);
    setFailed(false);
    try {
      await invoke(SeelenCommand.SetDesktopSuiteComponent, { module, enabled });
      await refresh();
    } catch { setFailed(true); }
    finally { setPending(null); }
  }

  async function startup(enabled: boolean) {
    setPending("startup");
    setFailed(false);
    try {
      await invoke(SeelenCommand.SetAutoStart, { enabled });
      await refresh();
    } catch { setFailed(true); }
    finally { setPending(null); }
  }

  async function quit() {
    setPending("exit");
    setFailed(false);
    try { await invoke(SeelenCommand.ExitDesktopSuite); }
    catch { setFailed(true); setPending(null); }
  }

  useEffect(() => {
    let mounted = true;
    invoke(SeelenCommand.GetDesktopSuiteModules).then((modules) => {
      if (mounted) setAvailable(modules);
    }).catch(() => {
      if (mounted) setFailed(true);
    });
    return () => {
      mounted = false;
    };
  }, [loadAttempt]);

  async function openModule(module: string) {
    if (pending) return;
    setFailed(false);
    setPending(module);
    try {
      await invoke(SeelenCommand.OpenDesktopSuiteModule, { module });
    } catch {
      setFailed(true);
    } finally {
      setPending(null);
    }
  }

  return (
    <>
      {failed && (
        <Alert
          type="error"
          showIcon
          title={t("desktop_suite.open_failed")}
          action={
            <Button
              onClick={() => {
                setFailed(false);
                setAvailable(null);
                setLoadAttempt((attempt) => attempt + 1);
              }}
            >
              {t("desktop_suite.retry")}
            </Button>
          }
        />
      )}
      <SettingsGroup>
        <SettingsOption label={t("desktop_suite.host")} description={t("desktop_suite.host_description")} />
        <SettingsOption
          label={t("general.startup")}
          description={t("desktop_suite.startup_description")}
          action={<Switch aria-label={t("general.startup")} checked={suite?.autostart ?? false}
            disabled={!suite || pending !== null} loading={pending === "startup"} onChange={startup} />}
        />
      </SettingsGroup>
      <SettingsGroup>
        {["stats", "touchpad"].map((module) => {
          const component = suite?.components.find((row) => row.id === module);
          return <SettingsOption key={module} label={t(`desktop_suite.${module}`)}
            description={component?.error ? t("desktop_suite.component_failed")
              : !component ? t("desktop_suite.checking")
              : !component.enabled ? t(`desktop_suite.${module}_disabled`)
              : component.running ? t("desktop_suite.running") : t("desktop_suite.not_running")}
            action={<Flex gap="small" align="center">
              {component?.enabled && !component.running && <Button disabled={pending !== null}
                onClick={() => change(module, true)}>{t("desktop_suite.retry")}</Button>}
              <Button disabled={!component?.available || pending !== null}
                onClick={() => navigate(`/${module}`)}>{t("desktop_suite.configure")}</Button>
              <Switch aria-label={t(`desktop_suite.${module}`)} checked={component?.enabled ?? false}
                disabled={!component || (!component.available && !component.enabled) || pending !== null}
                loading={pending === module} onChange={(enabled) => change(module, enabled)} />
            </Flex>} />;
        })}
      </SettingsGroup>
      <SettingsGroup>
        {["activity_monitor"].map((module) => (
          <SettingsOption
            key={module}
            label={t(`desktop_suite.${module}`)}
            description={t(`desktop_suite.${module}_description`)}
            action={
              <Button
                disabled={!available?.includes(module) || (pending !== null && pending !== module)}
                loading={pending === module}
                onClick={() => openModule(module)}
              >
                {available === null
                  ? t("desktop_suite.checking")
                  : available.includes(module)
                  ? t("open")
                  : t("desktop_suite.unavailable")}
              </Button>
            }
          />
        ))}
      </SettingsGroup>
      <SettingsGroup>
        {["weg", "toolbar"].map((widget) => (
          <SettingsOption
            key={widget}
            label={t(`desktop_suite.${widget}`)}
            description={t(`desktop_suite.${widget}_description`)}
            action={<Button onClick={() => navigate(`/widget?id=@seelen/${widget}`)}>{t("open")}</Button>}
          />
        ))}
        <SettingsOption label={t("desktop_suite.replace_taskbar")}
          description={t(suite?.taskbar?.active ? "desktop_suite.taskbar_active" : "desktop_suite.taskbar_recovery")} />
      </SettingsGroup>
      <SettingsGroup>
        <SettingsOption label={t("desktop_suite.upstream")} description={t("desktop_suite.integration_boundary")} />
        <SettingsOption label={t("desktop_suite.quit")} description={t("desktop_suite.quit_description")}
          action={<Popconfirm title={t("desktop_suite.quit")} description={t("desktop_suite.quit_description")}
            onConfirm={quit} okText={t("desktop_suite.quit")}>
            <Button danger disabled={pending !== null} loading={pending === "exit"}>{t("desktop_suite.quit")}</Button>
          </Popconfirm>} />
      </SettingsGroup>
    </>
  );
}
