import React, { useEffect } from "react";
import { Column, DataTable } from "../ui/DataTable";
import type { McpCallDto } from "../../api/generated/types.gen";
import { ErrorBlock, LoadingBlock } from "../common/QueryState";
import { Pill } from "../common/Badges";
import { useAppDispatch, useAppSelector } from "../../store/hooks";
import { fetchMcp } from "../../store/slices/settingsSlice";
import { formatDateTime } from "../../utils/format";

const Line: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="min-w-0">
    <dt className="text-meta uppercase tracking-wide text-content-muted">{label}</dt>
    <dd className="m-0 text-sm break-all">{children}</dd>
  </div>
);

/** The MCP panel (admin only): where Claude connects, who may, and the last 20 audited calls. */
export const McpPanel: React.FC = () => {
  const dispatch = useAppDispatch();
  const { mcpInfo, mcpCalls, mcpStatus, mcpError } = useAppSelector((s) => s.settings);

  useEffect(() => {
    void dispatch(fetchMcp());
  }, [dispatch]);

  return (
    <section
      className="rounded-xl bg-surface border border-border p-4 space-y-3"
      aria-label="MCP endpoint"
      data-testid="mcp-panel"
    >
      <h2 className="text-base font-semibold">MCP endpoint for Claude</h2>
      {mcpStatus === "loading" && !mcpInfo && <LoadingBlock label="Loading MCP details" />}
      {mcpStatus === "failed" && (
        <ErrorBlock problem={mcpError} onRetry={() => void dispatch(fetchMcp())} title="Could not load the MCP panel" />
      )}
      {mcpInfo && (
        <dl className="grid grid-cols-1 md:grid-cols-2 gap-3 m-0">
          <Line label="Endpoint URL">{mcpInfo.endpointUrl ?? "Not configured"}</Line>
          <Line label="Allowed owner (subject)">{mcpInfo.ownerSubject ?? "Not configured (nobody can connect)"}</Line>
          <Line label="add_note tool">
            {mcpInfo.addNoteEnabled ? (
              <Pill tone="warn">On, needs {mcpInfo.addNoteRole}</Pill>
            ) : (
              <Pill tone="neutral">Off (read-only)</Pill>
            )}
          </Line>
          <Line label="Required role">{mcpInfo.requiredRole}</Line>
        </dl>
      )}
      <div>
        <h3 className="text-sm font-semibold mb-1">Last {mcpCalls.length > 0 ? mcpCalls.length : 20} calls</h3>
        <DataTable value={mcpCalls} dataKey="id" emptyMessage="No calls recorded yet." aria-label="MCP call log">
          <Column field="at" header="When" body={(c: McpCallDto) => formatDateTime(c.at)} />
          <Column field="tool" header="Tool" />
          <Column
            field="status"
            header="Status"
            body={(c: McpCallDto) => <Pill tone={c.status === "ok" ? "good" : "bad"}>{c.status}</Pill>}
          />
          <Column field="durationMs" header="ms" align="right" />
          <Column field="caller" header="Caller" />
          <Column
            field="arguments"
            header="Arguments"
            body={(c: McpCallDto) => <span className="font-numeric text-meta break-all">{c.arguments ?? ""}</span>}
          />
        </DataTable>
      </div>
    </section>
  );
};
