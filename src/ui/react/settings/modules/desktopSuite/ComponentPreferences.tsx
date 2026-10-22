import { Alert, Button, Collapse, ColorPicker, Flex, InputNumber, Modal, Select, Slider, Spin, Switch } from "antd";
import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { SettingsGroup, SettingsOption } from "../../components/SettingsBox/index.tsx";
import { componentEditors, componentSavePending, discardComponentPreferences, editComponentPreference, loadComponentPreferences, type ComponentId } from "../../state/componentPreferences.ts";
import { isDirty, type JsonValue } from "./preferencesDraft.ts";
import { Icon } from "libs/ui/react/components/Icon/index.tsx";
import cs from "./preferences.module.css";

interface Metric { [key: string]: JsonValue; Id: string; IsEnabled: boolean; DisplayStyle: string; Accent: string }
interface Rule { [key: string]: JsonValue; MetricId: string; IsEnabled: boolean; Threshold: number; Comparison: string; SustainSeconds: number }

export function StatsPreferences() { return <ComponentPreferences module="stats" />; }
export function TouchpadPreferences() { return <ComponentPreferences module="touchpad" />; }

function ComponentPreferences({ module }: { module: ComponentId }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  useEffect(() => { void loadComponentPreferences(module); if (module === "touchpad") void loadComponentPreferences("tracking"); }, [module]);
  const editor = componentEditors.value[module];
  const data = editor.draft;
  const dirty = editor.snapshot !== null && isDirty(editor.snapshot.data, data);
  const disabled = componentSavePending.value || editor.loading || !editor.snapshot;
  const edit = (key: string, value: unknown) => editComponentPreference(module, key, value);
  const number = (key: string, min: number, max: number, step = 1) => <InputNumber
    aria-label={t(`suite_preferences.${key}`)} min={min} max={max} step={step} precision={0}
    value={Number(data[key])} disabled={disabled} onChange={(value) => { if (value !== null) edit(key, value); }} />;
  const toggle = (key: string) => <Switch aria-label={t(`suite_preferences.${key}`)} checked={data[key] === true}
    disabled={disabled} onChange={(value) => edit(key, value)} />;
  const metricName = (id: string) => t(`suite_preferences.metrics.${id}`, { defaultValue: id });
  const metrics = (data.Metrics ?? []) as Metric[];
  const rules = (data.Alerts ?? []) as Rule[];
  const tracking = componentEditors.value.tracking;
  const reloadFailed = (id: ComponentId | "tracking") => Modal.confirm({
    title: t("suite_autosave.reload_title"), content: t("suite_autosave.reload_description"),
    okText: t("suite_preferences.reload"), cancelText: t("cancel"), autoFocusButton: "cancel",
    onOk: async () => { discardComponentPreferences(id); await loadComponentPreferences(id, true); },
  });
  const setMetric = (index: number, patch: Partial<Metric>) => edit("Metrics", metrics.map((row, i) => i === index ? { ...row, ...patch } : row));
  const moveMetric = (index: number, offset: number) => {
    const target = index + offset;
    if (target < 0 || target >= metrics.length) return;
    const next = [...metrics];
    [next[index], next[target]] = [next[target]!, next[index]!];
    edit("Metrics", next);
  };
  const setRule = (index: number, patch: Partial<Rule>) => edit("Alerts", rules.map((row, i) => i === index ? { ...row, ...patch } : row));

  return <div className={cs.page}>
    <SettingsGroup>
      <SettingsOption label={t(`desktop_suite.${module}`)} description={t(`suite_preferences.${module}_intro`)}
        action={<Button disabled={dirty || componentSavePending.value || editor.loading}
          onClick={() => loadComponentPreferences(module, true)}>{t("suite_preferences.reload")}</Button>} />
    </SettingsGroup>
    {editor.loading && <Spin aria-label={t("desktop_suite.checking")} />}
    {editor.error && <Alert type="error" showIcon title={t("suite_preferences.failed")}
      description={t("suite_preferences.failed_description")}
      action={<Button disabled={componentSavePending.value} onClick={() => reloadFailed(module)}>{t("suite_preferences.reload")}</Button>} />}
    {editor.saved && <Alert type="success" showIcon title={t("suite_preferences.saved")} />}
    {editor.snapshot && !editor.snapshot.enabled && <Alert type="info" showIcon title={t("suite_preferences.disabled")}
      description={t("suite_preferences.disabled_description")} action={<Button onClick={() => navigate("/")}>{t("desktop_suite.title")}</Button>} />}
    {dirty && <Alert type="info" showIcon title={t("suite_preferences.unsaved")}
      description={t(module === "touchpad" && editor.snapshot?.running ? "suite_preferences.touchpad_apply" : "suite_preferences.save_hint")} />}
    {editor.snapshot && (module === "stats" ? <>
      <SettingsGroup>
        <SettingsOption label={t("suite_preferences.RefreshIntervalMilliseconds")} action={number("RefreshIntervalMilliseconds", 500, 5000, 100)} />
        <SettingsOption label={t("suite_preferences.HistorySeconds")} action={number("HistorySeconds", 30, 300, 10)} />
        <SettingsOption label={t("suite_preferences.LowPowerMode")} action={toggle("LowPowerMode")} />
      </SettingsGroup>
      <SettingsGroup>
        <SettingsOption label={t("suite_preferences.menu_metrics")} description={t("suite_preferences.metric_hint")} />
        {metrics.map((row, index) => <SettingsOption key={row.Id} label={metricName(row.Id)} action={<Flex wrap gap="small" align="center">
          <Button aria-label={t("suite_preferences.move_up", { name: metricName(row.Id) })} disabled={disabled || index === 0}
            onClick={() => moveMetric(index, -1)}><Icon iconName="LuChevronUp" /></Button>
          <Button aria-label={t("suite_preferences.move_down", { name: metricName(row.Id) })} disabled={disabled || index === metrics.length - 1}
            onClick={() => moveMetric(index, 1)}><Icon iconName="LuChevronDown" /></Button>
          <Select aria-label={t("suite_preferences.display_style")} value={row.DisplayStyle} disabled={disabled}
            options={["stacked", "inline", "value", "chart"].map((value) => ({ value, label: t(`suite_preferences.styles.${value}`) }))}
            onChange={(value) => setMetric(index, { DisplayStyle: value })} />
          <ColorPicker aria-label={t("suite_preferences.color")} disabled={disabled} disabledAlpha
            value={row.Accent?.length === 9 ? `#${row.Accent.slice(3)}` : row.Accent}
            onChangeComplete={(color) => setMetric(index, { Accent: `#FF${color.toHexString().slice(1).toUpperCase()}` })} />
          <Switch aria-label={metricName(row.Id)} disabled={disabled} checked={row.IsEnabled}
            onChange={(value) => setMetric(index, { IsEnabled: value })} />
        </Flex>} />)}
      </SettingsGroup>
      <Collapse items={[{ key: "alerts", label: t("suite_preferences.alerts"), children: <SettingsGroup>
        {rules.map((rule, index) => <SettingsOption key={rule.MetricId} label={metricName(rule.MetricId)} action={<Flex wrap gap="small" align="center">
          <Select aria-label={t("suite_preferences.comparison")} value={rule.Comparison} disabled={disabled}
            options={["above", "below"].map((value) => ({ value, label: t(`suite_preferences.${value}`) }))}
            onChange={(value) => setRule(index, { Comparison: value })} />
          <label>{t("suite_preferences.threshold")}<InputNumber aria-label={t("suite_preferences.threshold")} min={0} max={rule.MetricId === "cpu_freq" ? 10000 : 200}
            value={rule.Threshold} disabled={disabled} onChange={(value) => { if (value !== null) setRule(index, { Threshold: value }); }} />
          </label><label>{t("suite_preferences.sustain")}<InputNumber aria-label={t("suite_preferences.sustain")} min={0} max={300} precision={0} value={rule.SustainSeconds}
            disabled={disabled} onChange={(value) => { if (value !== null) setRule(index, { SustainSeconds: value }); }} />
          </label>
          <Switch aria-label={metricName(rule.MetricId)} checked={rule.IsEnabled} disabled={disabled}
            onChange={(value) => setRule(index, { IsEnabled: value })} />
        </Flex>} />)}
      </SettingsGroup> }]} />
    </> : <>
      <SettingsGroup>
        <SettingsOption label={t("suite_preferences.ThreeFingerDrag")} action={toggle("ThreeFingerDrag")} />
        <SettingsOption label={t("suite_preferences.ThreeFingerDragButton")} action={<Select disabled={disabled}
          aria-label={t("suite_preferences.ThreeFingerDragButton")} value={Number(data.ThreeFingerDragButton)}
          options={["none", "left", "right", "middle"].map((button, value) => ({ value, label: t(`suite_preferences.buttons.${button}`) }))}
          onChange={(value) => edit("ThreeFingerDragButton", value)} />} />
        <SettingsOption label={t("suite_preferences.FourFingerGestures")} description={t("suite_preferences.four_finger_hint")}
          action={toggle("FourFingerGestures")} />
      </SettingsGroup>
      <SettingsGroup>
        <SettingsOption label={t("suite_preferences.tracking")} description={t("suite_preferences.tracking_description")}
          action={<Button disabled={componentSavePending.value || tracking.loading || !!tracking.snapshot && isDirty(tracking.snapshot.data, tracking.draft)}
            onClick={() => loadComponentPreferences("tracking", true)}>{t("suite_preferences.reload")}</Button>} />
        {tracking.loading && <Spin aria-label={t("desktop_suite.checking")} />}
        {tracking.error && <Alert type="error" showIcon title={t("suite_preferences.tracking_failed")} description={t("suite_preferences.failed_description")}
          action={<Button disabled={componentSavePending.value} onClick={() => reloadFailed("tracking")}>{t("suite_preferences.reload")}</Button>} />}
        {tracking.saved && <Alert type="success" showIcon title={t("suite_preferences.saved")} />}
        {tracking.snapshot && <>
          <SettingsOption label={t("suite_preferences.tracking_speed")} action={<Flex className={cs.trackingSpeed} gap="small" align="center">
            <Slider aria-label={t("suite_preferences.tracking_speed")} min={1} max={20} step={1}
              value={Number(tracking.draft.Speed)} disabled={componentSavePending.value || tracking.loading}
              onChange={(value) => editComponentPreference("tracking", "Speed", value)} />
            <InputNumber aria-label={t("suite_preferences.tracking_speed")} min={1} max={20} step={1} precision={0}
              value={Number(tracking.draft.Speed)} disabled={componentSavePending.value || tracking.loading}
              onChange={(value) => { if (value !== null) editComponentPreference("tracking", "Speed", value); }} />
          </Flex>} />
          <SettingsOption label={t("suite_preferences.acceleration")} description={t("suite_preferences.acceleration_warning")}
            action={<Switch aria-label={t("suite_preferences.acceleration")} checked={tracking.draft.Acceleration === true}
              disabled={componentSavePending.value || tracking.loading}
              onChange={(value) => editComponentPreference("tracking", "Acceleration", value)} />} />
        </>}
      </SettingsGroup>
      <Collapse items={[{ key: "gesture-timing", label: t("suite_preferences.advanced"), children: <SettingsGroup>
        <SettingsOption label={t("suite_preferences.ThreeFingerDragStartThreshold")} action={number("ThreeFingerDragStartThreshold", 0, 1000)} />
        <SettingsOption label={t("suite_preferences.FourFingerSwipeThreshold")} action={number("FourFingerSwipeThreshold", 40, 1000)} />
      </SettingsGroup> }]} />
      <SettingsGroup>
        <SettingsOption label={t("suite_preferences.Language")} action={<Select aria-label={t("suite_preferences.Language")}
          disabled={disabled} value={String(data.Language)} onChange={(value) => edit("Language", value)}
          options={[{ value: "system", label: t("suite_preferences.system") }, { value: "zh-Hans", label: "简体中文" }, { value: "en", label: "English" }]} />} />
        <SettingsOption label={t("suite_preferences.RecordLogs")} description={t("suite_preferences.logging_description")} action={toggle("RecordLogs")} />
      </SettingsGroup>
    </>)}
  </div>;
}
