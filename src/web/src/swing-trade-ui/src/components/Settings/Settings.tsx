import React, { useEffect, useState } from "react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faCheck, faRotateLeft } from "@fortawesome/free-solid-svg-icons";
import { Button } from "../ui/Button";
import type { SettingsDto } from "../../api/generated/types.gen";
import { PageHeader } from "../layout/PageHeader";
import { ErrorBlock, InlineMessage, LoadingBlock } from "../common/QueryState";
import { Pill } from "../common/Badges";
import { SelectField, TextField } from "../common/Fields";
import { McpPanel } from "./McpPanel";
import {
  DEFAULT_SETTINGS,
  FIELDS,
  RISK_BASIS_OPTIONS,
  toFormValues,
  toRequest,
  unitOf,
  type FormValues,
  type NumericKey,
} from "./settingsFields";
import { useAuth, useToast } from "../../context";
import { useAppDispatch, useAppSelector } from "../../store/hooks";
import { fetchSettings, saveSettings } from "../../store/slices/settingsSlice";

export const Settings: React.FC = () => {
  const dispatch = useAppDispatch();
  const toast = useToast();
  const { canAdmin, authInfo } = useAuth();
  const { settings, status, error } = useAppSelector((s) => s.settings);
  const [values, setValues] = useState<FormValues | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    void dispatch(fetchSettings());
  }, [dispatch]);

  useEffect(() => {
    if (settings) setValues(toFormValues(settings));
  }, [settings]);

  const setField = (key: NumericKey, text: string) => setValues((v) => (v ? { ...v, [key]: text } : v));

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!values) return;
    const parsed = toRequest(values);
    if (!parsed.request) {
      setFieldErrors(parsed.errors);
      setFormError("Please correct the highlighted fields.");
      return;
    }
    setSaving(true);
    setFieldErrors({});
    setFormError(null);
    const result = await dispatch(saveSettings(parsed.request));
    setSaving(false);
    if (saveSettings.fulfilled.match(result)) {
      toast.success("Settings saved", "Watchlist signals use the new thresholds on the next load.");
    } else if (result.payload) {
      setFieldErrors(result.payload.fieldErrors);
      setFormError(result.payload.message);
    }
  };

  const resetToDefaults = () => {
    setValues(toFormValues(DEFAULT_SETTINGS as SettingsDto));
    setFieldErrors({});
    setFormError(null);
    toast.info("Defaults loaded", "Review them, then choose Save settings to apply.");
  };

  return (
    <div className="space-y-4">
      <PageHeader
        title="Settings"
        subtitle={
          canAdmin
            ? "Thresholds apply immediately: Trend and Signal are derived from these values every time the watchlist loads."
            : "You can read the thresholds. Only an administrator can change them."
        }
      />

      {status === "loading" && !settings && <LoadingBlock label="Loading settings" />}
      {status === "failed" && (
        <ErrorBlock problem={error} onRetry={() => void dispatch(fetchSettings())} title="Could not load settings" />
      )}

      {values && (
        <form
          onSubmit={(e) => void save(e)}
          noValidate
          className="rounded-xl bg-surface border border-border p-4 space-y-4"
          aria-label="Thresholds"
        >
          {!canAdmin && (
            <InlineMessage severity="info" testId="settings-readonly">
              Read-only: changing settings needs the swingtrade.admin role.
            </InlineMessage>
          )}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-4">
            {FIELDS.map((f) => (
              <div key={f.key}>
                <TextField
                  id={`setting-${f.key}`}
                  label={`${f.label}${unitOf(f.kind) ? ` (${unitOf(f.kind)})` : ""}`}
                  value={values[f.key]}
                  onChange={(v) => setField(f.key, v)}
                  inputMode="decimal"
                  disabled={!canAdmin}
                  error={fieldErrors[f.key]}
                  hint={f.help}
                />
                {f.assumption && (
                  <div className="mt-1">
                    <Pill
                      tone="warn"
                      testId="assumption-flag"
                      title="This rate is an assumption, not tax advice. Confirm it with your own bracket."
                    >
                      Assumption: confirm your own rate
                    </Pill>
                  </div>
                )}
              </div>
            ))}
            <SelectField
              id="setting-riskBasis"
              label="Risk basis"
              value={values.riskBasis}
              options={RISK_BASIS_OPTIONS}
              onChange={(v) => setValues((s) => (s ? { ...s, riskBasis: v } : s))}
              disabled={!canAdmin}
              error={fieldErrors.riskBasis}
            />
          </div>
          {formError && <InlineMessage severity="error">{formError}</InlineMessage>}
          {canAdmin && (
            <div className="flex flex-wrap gap-2">
              <Button type="submit" label="Save settings" icon={<FontAwesomeIcon icon={faCheck} />} loading={saving} />
              <Button
                type="button"
                label="Reset to defaults"
                icon={<FontAwesomeIcon icon={faRotateLeft} />}
                variant="outlined"
                onClick={resetToDefaults}
              />
            </div>
          )}
        </form>
      )}

      <section className="rounded-xl bg-surface border border-border p-4" aria-label="Access">
        <h2 className="text-base font-semibold mb-2">Access</h2>
        {authInfo ? (
          <dl className="grid grid-cols-1 md:grid-cols-2 gap-3 m-0 text-sm" data-testid="access-info">
            <div>
              <dt className="text-meta uppercase tracking-wide text-content-muted">Your roles</dt>
              <dd className="m-0">{authInfo.roles.join(", ") || "None"}</dd>
            </div>
            <div>
              <dt className="text-meta uppercase tracking-wide text-content-muted">API audience</dt>
              <dd className="m-0">{authInfo.audience}</dd>
            </div>
            <div>
              <dt className="text-meta uppercase tracking-wide text-content-muted">Roles, from least to most access</dt>
              <dd className="m-0">
                {authInfo.readerRole}, {authInfo.traderRole}, {authInfo.adminRole}
              </dd>
            </div>
            <div>
              <dt className="text-meta uppercase tracking-wide text-content-muted">
                Policies (Read, Write, Admin, Mcp)
              </dt>
              <dd className="m-0">
                {authInfo.readPolicy}, {authInfo.writePolicy}, {authInfo.adminPolicy}, {authInfo.mcpPolicy} (role{" "}
                {authInfo.mcpRole})
              </dd>
            </div>
          </dl>
        ) : (
          <p className="text-sm text-content-muted">Role details are not available.</p>
        )}
        <p className="text-meta text-content-muted mt-2">
          Roles are granted in the identity server. The API enforces them; this screen only shows the names.
        </p>
      </section>

      {canAdmin && <McpPanel />}
    </div>
  );
};
