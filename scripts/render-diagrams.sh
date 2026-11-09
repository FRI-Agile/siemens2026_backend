#!/usr/bin/env bash
# Renders every PlantUML diagram in docs/diagrams/architecture and docs/diagrams/api to SVG.
# Uses PlantUML's built-in Smetana layout, so Graphviz is not required.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
diagrams=()
for f in "$root"/docs/diagrams/architecture/*.plantuml "$root"/docs/diagrams/api/*.plantuml; do
    [[ "$(basename "$f")" == include.plantuml ]] || diagrams+=("$f")
done
java -jar "$root/scripts/tools/plantuml.jar" -Playout=smetana -tsvg "${diagrams[@]}"
