import React, { useEffect, useRef } from "react";
import { Chart, type ChartConfiguration } from "chart.js/auto";

export interface ChartSeries {
  label: string;
  values: (number | null)[];
  color: string;
  /** Draw as a thin dashed line (moving averages). */
  dashed?: boolean;
  fill?: boolean;
}

interface LineChartProps {
  labels: string[];
  series: ChartSeries[];
  ariaLabel: string;
  height?: number;
  /** Formats the y-axis ticks and the tooltip values (money in the UI). */
  formatValue?: (value: number) => string;
}

/** A Chart.js line chart: the points are exactly the values the API returned. */
export const LineChart: React.FC<LineChartProps> = ({ labels, series, ariaLabel, height = 280, formatValue }) => {
  const canvas = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const element = canvas.current;
    if (!element) return;
    const config: ChartConfiguration<"line"> = {
      type: "line",
      data: {
        labels,
        datasets: series.map((s) => ({
          label: s.label,
          data: s.values,
          borderColor: s.color,
          backgroundColor: s.fill ? `${s.color}22` : s.color,
          borderWidth: s.dashed ? 1.5 : 2,
          borderDash: s.dashed ? [5, 4] : undefined,
          pointRadius: 0,
          pointHoverRadius: 4,
          tension: 0,
          spanGaps: false,
          fill: !!s.fill,
        })),
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: "index", intersect: false },
        plugins: {
          legend: { position: "bottom", labels: { usePointStyle: true } },
          tooltip: {
            callbacks: {
              label: (item) =>
                `${item.dataset.label}: ${formatValue && item.parsed.y !== null ? formatValue(item.parsed.y) : item.formattedValue}`,
            },
          },
        },
        scales: {
          x: { ticks: { maxTicksLimit: 8, autoSkip: true } },
          y: { ticks: { callback: (v) => (formatValue ? formatValue(Number(v)) : String(v)) } },
        },
      },
    };
    const chart = new Chart(element, config);
    return () => chart.destroy();
  }, [labels, series, formatValue]);

  return (
    <div style={{ height }} className="relative w-full">
      <canvas ref={canvas} role="img" aria-label={ariaLabel} />
    </div>
  );
};
