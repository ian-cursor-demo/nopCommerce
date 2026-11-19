#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GLOSSARY="${ROOT_DIR}/src/Presentation/Nop.Web/App_Data/Localization/fr-FR/checkout-glossary.yml"
FRENCH_XML="${ROOT_DIR}/src/Presentation/Nop.Web/App_Data/Localization/fr-FR/checkout.xml"

if [[ ! -f "$GLOSSARY" ]]; then
  echo "Glossary not found: $GLOSSARY" >&2
  exit 1
fi

if [[ ! -f "$FRENCH_XML" ]]; then
  echo "French checkout resources not found: $FRENCH_XML" >&2
  exit 1
fi

frenchValues=()
while IFS= read -r line; do
  if [[ $line =~ \<Value\>(.*)\</Value\> ]]; then
    frenchValues+=("${BASH_REMATCH[1]}")
  fi
done < "$FRENCH_XML"

failures=0
term=""
use_instead=""

while IFS= read -r line; do
  if [[ $line =~ term:[[:space:]]*\"(.+)\" ]]; then
    term="${BASH_REMATCH[1]}"
  elif [[ $line =~ use_instead:[[:space:]]*\"(.+)\" ]]; then
    use_instead="${BASH_REMATCH[1]}"
    if [[ -n "$term" ]]; then
      lower_term="$(printf '%s' "$term" | tr '[:upper:]' '[:lower:]')"
      for value in "${frenchValues[@]}"; do
        lower_value="$(printf '%s' "$value" | tr '[:upper:]' '[:lower:]')"
        if [[ "$lower_value" == *"$lower_term"* ]]; then
          echo "FORBIDDEN TERM: '$term' found in French value: $value"
          echo "  Use approved term instead: $use_instead"
          failures=$((failures + 1))
        fi
      done
      term=""
      use_instead=""
    fi
  fi
done < "$GLOSSARY"

if [[ $failures -gt 0 ]]; then
  echo "Terminology check failed with $failures violation(s)." >&2
  exit 1
fi

echo "Terminology check passed."
