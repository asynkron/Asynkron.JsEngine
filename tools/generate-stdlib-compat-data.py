#!/usr/bin/env python3
import argparse
import datetime
import io
import json
import os
import tarfile
import urllib.request

DEFAULT_TAG = "v7.2.3"
REPO = "mdn/browser-compat-data"

IGNORE_MEMBER_NAMES = {
    "constructor",
    "length",
    "name",
    "prototype",
}

ALLOWED_MDN_PREFIX = (
    "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/"
)


def download_tarball(tag: str) -> tarfile.TarFile:
    url = f"https://api.github.com/repos/{REPO}/tarball/{tag}"
    request = urllib.request.Request(
        url, headers={"Accept": "application/vnd.github+json"}
    )
    with urllib.request.urlopen(request) as response:
        data = response.read()
    return tarfile.open(fileobj=io.BytesIO(data), mode="r:gz")


def is_standard_track(compat: dict) -> bool:
    status = compat.get("status") or {}
    return bool(status.get("standard_track"))


def is_accessible_member(compat: dict) -> bool:
    return bool(get_url_value(compat, "mdn_url") or get_url_value(compat, "spec_url"))


def get_url_value(compat: dict, key: str) -> str:
    value = compat.get(key)
    if isinstance(value, str):
        return value
    if isinstance(value, list):
        for entry in value:
            if isinstance(entry, str):
                return entry
            if isinstance(entry, dict) and isinstance(entry.get("url"), str):
                return entry["url"]
    return ""


def classify_member(member_name: str, compat: dict) -> tuple[str, str | None, str]:
    spec_url = get_url_value(compat, "spec_url").lower()
    mdn_url = get_url_value(compat, "mdn_url").lower()
    url_probe = spec_url or mdn_url

    location = "prototype" if "prototype" in url_probe else "constructor"
    symbol_name = None
    property_name = member_name

    if member_name.startswith("@@"):
        symbol_name = member_name[2:]
        property_name = symbol_name

    if "sec-get-" in spec_url:
        kind = "getter"
    elif "sec-set-" in spec_url:
        kind = "setter"
    else:
        kind = "method"

    return location, symbol_name, kind


def is_constant_name(member_name: str) -> bool:
    return member_name.upper() == member_name and any(
        ch.isalpha() for ch in member_name
    )


def should_skip_member(member_name: str, compat: dict) -> bool:
    if member_name in IGNORE_MEMBER_NAMES:
        return True

    if is_constant_name(member_name):
        return True

    mdn_url = get_url_value(compat, "mdn_url")
    if mdn_url and not mdn_url.startswith(ALLOWED_MDN_PREFIX):
        return True

    spec_url = get_url_value(compat, "spec_url")
    spec_fragment = spec_url.split("#", 1)[1] if "#" in spec_url else spec_url
    spec_fragment = spec_fragment.lower()
    if "sec-properties-of-" in spec_fragment:
        return True

    if spec_fragment and not any(
        token in spec_fragment for token in (".", "prototype", "sec-get-", "sec-set-")
    ):
        return True

    return False


def add_member(
    bucket: dict, location: str, kind: str, name: str, symbol_name: str | None
) -> None:
    target = bucket[location]
    if symbol_name:
        key = f"symbol_{kind}s"
        target[key].add(symbol_name)
        return

    key = f"{kind}s"
    target[key].add(name)


def normalize_builtin(builtin_name: str, builtin_node: dict, output: dict) -> None:
    if builtin_name == "globals":
        return

    builtin_bucket = output.setdefault(
        builtin_name,
        {
            "constructor": {
                "methods": set(),
                "getters": set(),
                "setters": set(),
                "symbol_methods": set(),
                "symbol_getters": set(),
                "symbol_setters": set(),
            },
            "prototype": {
                "methods": set(),
                "getters": set(),
                "setters": set(),
                "symbol_methods": set(),
                "symbol_getters": set(),
                "symbol_setters": set(),
            },
        },
    )

    for member_name, member_node in builtin_node.items():
        if member_name == "__compat":
            continue

        compat = member_node.get("__compat") if isinstance(member_node, dict) else None
        if not compat:
            continue

        if not is_standard_track(compat):
            continue

        if not is_accessible_member(compat):
            continue

        if member_name == builtin_name:
            continue

        if should_skip_member(member_name, compat):
            continue

        location, symbol_name, kind = classify_member(member_name, compat)
        add_member(builtin_bucket, location, kind, member_name, symbol_name)


def to_sorted_list(values: set[str]) -> list[str]:
    return sorted(values, key=str.casefold)


def finalize_output(collected: dict) -> dict:
    result: dict[str, dict] = {}
    for builtin_name, buckets in sorted(
        collected.items(), key=lambda item: item[0].casefold()
    ):
        result[builtin_name] = {
            "constructor": {
                "methods": to_sorted_list(buckets["constructor"]["methods"]),
                "getters": to_sorted_list(buckets["constructor"]["getters"]),
                "setters": to_sorted_list(buckets["constructor"]["setters"]),
                "symbolMethods": to_sorted_list(
                    buckets["constructor"]["symbol_methods"]
                ),
                "symbolGetters": to_sorted_list(
                    buckets["constructor"]["symbol_getters"]
                ),
                "symbolSetters": to_sorted_list(
                    buckets["constructor"]["symbol_setters"]
                ),
            },
            "prototype": {
                "methods": to_sorted_list(buckets["prototype"]["methods"]),
                "getters": to_sorted_list(buckets["prototype"]["getters"]),
                "setters": to_sorted_list(buckets["prototype"]["setters"]),
                "symbolMethods": to_sorted_list(buckets["prototype"]["symbol_methods"]),
                "symbolGetters": to_sorted_list(buckets["prototype"]["symbol_getters"]),
                "symbolSetters": to_sorted_list(buckets["prototype"]["symbol_setters"]),
            },
        }
    return result


def collect_data(tag: str) -> dict:
    collected: dict[str, dict] = {}
    with download_tarball(tag) as tar:
        for member in tar.getmembers():
            if not member.isfile():
                continue
            if "/javascript/builtins/" not in member.name:
                continue
            if not member.name.endswith(".json"):
                continue

            file_handle = tar.extractfile(member)
            if file_handle is None:
                continue

            payload = json.load(file_handle)
            builtins = payload.get("javascript", {}).get("builtins", {})
            if not isinstance(builtins, dict):
                continue

            for builtin_name, builtin_node in builtins.items():
                if isinstance(builtin_node, dict):
                    normalize_builtin(builtin_name, builtin_node, collected)

    return collected


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--tag", default=DEFAULT_TAG)
    parser.add_argument(
        "--output",
        default=os.path.join(
            os.path.dirname(__file__),
            "..",
            "src",
            "Asynkron.JsEngine.Generators",
            "Data",
            "stdlib-compat.json",
        ),
    )
    args = parser.parse_args()

    collected = collect_data(args.tag)
    output = {
        "source": {
            "repository": REPO,
            "tag": args.tag,
        },
        "generatedAt": datetime.datetime.now(datetime.UTC)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z"),
        "builtins": finalize_output(collected),
    }

    output_path = os.path.abspath(args.output)
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as handle:
        json.dump(output, handle, indent=2, sort_keys=False)
        handle.write("\n")

    print(f"Wrote {output_path}")


if __name__ == "__main__":
    main()
