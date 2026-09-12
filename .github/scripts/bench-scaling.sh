#!/usr/bin/env bash
# Renders scaling curves from datapoints emitted by bench-aggregate.sh across runs.
# Each size-<N>-server.json / size-<N>-clients.json holds per-scenario metrics; this renders,
# per benchmark scenario, a table plus Mermaid line charts (CPU / RTT / bandwidth vs connections).
#
# Usage: bench-scaling.sh <datapoints_dir> <window_s> <objects>
set -euo pipefail

DP_DIR="${1:?datapoints dir}"
WINDOW="${2:-?}"
OBJECTS="${3:-?}"

SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

shopt -s nullglob
FILES=("$DP_DIR"/size-*-server.json "$DP_DIR"/size-*-clients.json)

{
  echo "## Benchmark Scaling Curve"
  echo ""
  echo "Window: ${WINDOW}s · Objects: ${OBJECTS}"
  echo ""
} >> "$SUMMARY"

if [ ${#FILES[@]} -eq 0 ]; then
  echo "_No datapoints collected._" >> "$SUMMARY"
  exit 0
fi

jq -rs '
  def hbR:
    if . == null then "-"
    elif . < 1024 then "\(.|floor) B/s"
    elif . < 1048576 then "\((.*10/1024|floor)/10) KB/s"
    else "\((.*100/1048576|floor)/100) MB/s" end;
  def r2: if . == null then "-" else "\((.*100|floor)/100) ms" end;
  def r1: if . == null then "-" else "\((.*10|floor)/10)%" end;
  def mb: if . == null then "-" else "\((./1048576)|floor) MB" end;
  def avg(f): if length == 0 then null else (map(f) | add) / length end;
  def d1: (. // 0) | (.*10|floor)/10;
  # A Mermaid xychart-beta line chart. $xs = category labels, $ys = numeric series.
  def chart($title; $ylabel; $xs; $ys):
    ( ($ys | map(. // 0) | max) as $m
      | (($m * 1.1 | floor) + 1) as $top
      | "```mermaid\n"
        + "xychart-beta\n"
        + "    title \"\($title)\"\n"
        + "    x-axis [\($xs | map(tostring) | join(", "))]\n"
        + "    y-axis \"\($ylabel)\" 0 --> \($top)\n"
        + "    line [\($ys | map(. // 0 | tostring) | join(", "))]\n"
        + "```\n" );

  # server[scenario][connections] = server benchmark ; client[scenario][connections] = measured[]
  (reduce (.[] | select(has("serverScenarios"))) as $f ({};
     reduce ($f.serverScenarios | to_entries[]) as $e (.;
       .[$e.key][$f.connections | tostring] = $e.value))) as $srv
  | (reduce (.[] | select(has("clientScenarios"))) as $f ({};
       reduce ($f.clientScenarios | to_entries[]) as $e (.;
         .[$e.key][$f.connections | tostring] = $e.value))) as $cli
  | ([ $srv, $cli | keys[] ] | unique) as $scenarios
  | $scenarios[]
  | . as $name
  | (($srv[$name] // {}) + ($cli[$name] // {}) | keys | map(tonumber) | unique | sort) as $conns
  | [ $conns[] | ($srv[$name][(.|tostring)] // {}) ] as $srows
  | [ $conns[] | ($cli[$name][(.|tostring)] // []) ] as $crows
  | [ $srows[] | .serverCpuPercent | d1 ] as $cpu
  | [ $srows[] | (.onWireSentBytesPerSec // 0) / 1024 | d1 ] as $bw
  | [ $crows[] | avg(.rttP95Ms) | d1 ] as $rtt
  | "### \($name)\n\n"
    + "| Connections | Down payload | Down on-wire | Overhead | CPU | Loop rate | Frame p95 | GC | Heap | Peak RSS | Per-conn down | RTT p95 |\n"
    + "|---|---|---|---|---|---|---|---|---|---|---|---|\n"
    + ( [ range(0; $conns|length) as $i
          | $conns[$i] as $n | ($srows[$i]) as $s | ($crows[$i]) as $c
          | "| \($n) "
            + "| \($s.sentBytesPerSec|hbR) "
            + "| \($s.onWireSentBytesPerSec|hbR) "
            + "| \($s.framingOverheadPercent|r1) "
            + "| \($s.serverCpuPercent|r1) "
            + "| \($s.avgFps|if . == null then "-" else floor end) fps "
            + "| \($s.p95TickMs|r2) "
            + "| \($s.gcCollections // "-") "
            + "| \($s.managedHeapBytes|mb) "
            + "| \($s.peakMemoryBytes|mb) "
            + "| \($c | avg(.receivedBytesPerSec) | hbR) "
            + "| \($c | avg(.rttP95Ms) | r2) |"
        ] | join("\n") )
    + "\n\n"
    # Per-marker CPU scaling (Development builds only; release builds have no markers).
    # This is the table that shows which packing/encode phase scales with player count:
    # read across a row — a phase that grows ~linearly with connections is an O(players) loop.
    + ( ([ $srows[] | (.cpuMarkers // []) | map(.name) ] | add // [] | unique) as $allNames
        | if ($allNames | length) == 0 then "" else
          ($allNames
            | map(. as $n | {name: $n, peak: ([ $srows[] | (.cpuMarkers // [])[] | select(.name == $n) | .perFrameMs ] | max // 0)})
            | sort_by(-.peak) | .[0:14] | map(.name)) as $top
          | "#### Server CPU µs/frame by marker vs connections — \($name) (Development build)\n\n"
          + "| Marker | \($conns | map(tostring) | join(" | ")) |\n"
          + "|---|\($conns | map("---") | join("|"))|\n"
          + ( [ $top[] | . as $mk
                | "| \($mk) | "
                  + ( [ $srows[]
                        | ((.cpuMarkers // []) | map(select(.name == $mk)) | first) as $entry
                        | if $entry == null then "-" else "\($entry.perFrameMs * 1000 | floor)" end
                      ] | join(" | ") )
                  + " |"
              ] | join("\n") )
          + "\n\n"
          end )
    + ( if ($conns | length) < 2 then "_Charts need ≥2 connection sizes._\n"
        else chart("CPU % vs connections — \($name)"; "CPU %"; $conns; $cpu)
           + chart("RTT p95 (ms) vs connections — \($name)"; "RTT p95 (ms)"; $conns; $rtt)
           + chart("Server downstream KB/s vs connections — \($name)"; "KB/s on-wire"; $conns; $bw)
        end )
    + "\n"
' "${FILES[@]}" >> "$SUMMARY" || echo "_Failed to render datapoints._" >> "$SUMMARY"

echo "" >> "$SUMMARY"
