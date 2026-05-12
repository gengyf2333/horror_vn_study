import argparse
import base64
import json
from pathlib import Path
from openai import OpenAI


# =========================
# use your own api key and settings
# =========================

OPENAI_API_KEY = "sk-proj-xxxxxxxx"

IMAGE_MODEL = "gpt-image-1"

DEFAULT_INPUT_PATH = "outputs/asset_prompts_horrorified.json"
DEFAULT_OUTPUT_DIR = "outputs/images/horrorified"

BACKGROUND_SIZE_HINT = "1536x1024"
CHARACTER_SIZE_HINT = "1024x1536"


def read_json(path: str):
    path_obj = Path(path)
    if not path_obj.exists():
        raise FileNotFoundError(f"File not found: {path}")
    return json.loads(path_obj.read_text(encoding="utf-8"))


def save_base64_image(b64_data: str, save_path: Path):
    image_bytes = base64.b64decode(b64_data)
    save_path.parent.mkdir(parents=True, exist_ok=True)
    with open(save_path, "wb") as f:
        f.write(image_bytes)


def generate_one_image(client: OpenAI, prompt: str, size: str):
    result = client.images.generate(
        model=IMAGE_MODEL,
        prompt=prompt,
        size=size
    )
    return result.data[0].b64_json
def infer_base_character_id(asset_id: str) -> str:
    # mira_neutral -> mira
    # hana_thinking -> hana
    if "_" not in asset_id:
        return asset_id
    return asset_id.split("_")[0]


def choose_baseline_character_item(items: list):
    # choose neutral as baseline
    priority = ["neutral", "happy", "thinking", "default"]

    for p in priority:
        for item in items:
            if item["asset_id"].endswith(f"_{p}"):
                return item

    return items[0]


def group_characters_by_base(characters: list):
    groups = {}

    for item in characters:
        asset_id = item["asset_id"]
        base_id = infer_base_character_id(asset_id)
        groups.setdefault(base_id, []).append(item)

    return groups

def edit_one_image(client: OpenAI, image_path: Path, prompt: str, size: str):
    with open(image_path, "rb") as f:
        result = client.images.edit(
            model=IMAGE_MODEL,
            image=f,
            prompt=prompt,
            size=size
        )
    return result.data[0].b64_json
#python code/generate_image.py --input outputs/asset_prompts_horrorified.json --outdir outputs/images/horrorified --only all
#python code/generate_image.py --input outputs/asset_prompts_neutral.json --outdir outputs/images/neutral --only characters
#python code/generate_image.py --input outputs/asset_prompts_horrorified.json --outdir outputs/images/horrorified --only characters
def generate_backgrounds(client: OpenAI, backgrounds: list, output_dir: Path):
    bg_dir = output_dir / "backgrounds"
    bg_dir.mkdir(parents=True, exist_ok=True)

    failed = []

    for item in backgrounds:
        asset_id = item["asset_id"]
        prompt = item["prompt"]

        save_path = bg_dir / f"{asset_id}.png"

        if save_path.exists():
            print(f"[BG] Skip existing: {save_path}")
            continue

        print(f"[BG] Generating: {asset_id}")

        try:
            b64_img = generate_one_image(
                client=client,
                prompt=prompt,
                size=BACKGROUND_SIZE_HINT
            )

            save_base64_image(b64_img, save_path)
            print(f"[BG] Saved: {save_path}")

        except Exception as e:
            print(f"[BG ERROR] Failed: {asset_id}")
            print(e)

            failed.append({
                "asset_id": asset_id,
                "prompt": prompt,
                "error": str(e)
            })

            continue

    if failed:
        failed_path = output_dir / "failed_backgrounds.json"
        failed_path.write_text(
            json.dumps(failed, ensure_ascii=False, indent=2),
            encoding="utf-8"
        )
        print(f"[BG] Failed list saved to: {failed_path}")


def generate_characters(client: OpenAI, characters: list, output_dir: Path):
    char_dir = output_dir / "characters"
    baseline_dir = char_dir / "_baselines"
    char_dir.mkdir(parents=True, exist_ok=True)
    baseline_dir.mkdir(parents=True, exist_ok=True)

    failed = []
    groups = group_characters_by_base(characters)

    for base_id, items in groups.items():
        baseline_item = choose_baseline_character_item(items)
        baseline_asset_id = baseline_item["asset_id"]
        baseline_prompt = baseline_item["prompt"]

        baseline_path = baseline_dir / f"{base_id}.png"

        # 1) generate baseline
        if baseline_path.exists():
            print(f"[CHAR-BASE] Skip existing baseline: {baseline_path}")
        else:
            print(f"[CHAR-BASE] Generating baseline for: {base_id} ({baseline_asset_id})")

            try:
                baseline_b64 = generate_one_image(
                    client=client,
                    prompt=baseline_prompt,
                    size=CHARACTER_SIZE_HINT
                )
                save_base64_image(baseline_b64, baseline_path)
                print(f"[CHAR-BASE] Saved: {baseline_path}")

            except Exception as e:
                print(f"[CHAR-BASE ERROR] Failed baseline: {base_id}")
                print(e)

                failed.append({
                    "base_id": base_id,
                    "asset_id": baseline_asset_id,
                    "prompt": baseline_prompt,
                    "error": str(e)
                })
                continue

        # 2) base on  baseline generate difference
        for item in items:
            asset_id = item["asset_id"]
            prompt = item["prompt"]
            final_path = char_dir / f"{asset_id}.png"

            if final_path.exists():
                print(f"[CHAR] Skip existing: {final_path}")
                continue

            # if asset itself is baseline then copy
            if asset_id == baseline_asset_id:
                final_path.write_bytes(baseline_path.read_bytes())
                print(f"[CHAR] Saved baseline copy: {final_path}")
                continue

            print(f"[CHAR-VAR] Generating variant: {asset_id} from baseline {base_id}")

            variant_prompt = (
                "Edit the provided baseline character image to create a consistent character variant. "
                "Preserve identity, hairstyle, facial structure, clothing, proportions, and overall design. "
                "Only change the expression, subtle posture, and emotional tone needed for this specific sprite. "
                "Keep transparent background.\n\n"
                f"Target variant prompt:\n{prompt}"
            )

            try:
                variant_b64 = edit_one_image(
                    client=client,
                    image_path=baseline_path,
                    prompt=variant_prompt,
                    size=CHARACTER_SIZE_HINT
                )

                save_base64_image(variant_b64, final_path)
                print(f"[CHAR] Saved: {final_path}")

            except Exception as e:
                print(f"[CHAR ERROR] Failed: {asset_id}")
                print(e)

                failed.append({
                    "base_id": base_id,
                    "asset_id": asset_id,
                    "prompt": prompt,
                    "error": str(e)
                })
                continue

    if failed:
        failed_path = output_dir / "failed_characters.json"
        failed_path.write_text(
            json.dumps(failed, ensure_ascii=False, indent=2),
            encoding="utf-8"
        )
        print(f"[CHAR] Failed list saved to: {failed_path}")


def main():
    parser = argparse.ArgumentParser(
        description="Generate VN images from asset prompt list JSON."
    )

    parser.add_argument(
        "--input",
        default=DEFAULT_INPUT_PATH,
        help="Path to asset prompt JSON file."
    )

    parser.add_argument(
        "--outdir",
        default=DEFAULT_OUTPUT_DIR,
        help="Directory to save generated images."
    )

    parser.add_argument(
        "--only",
        choices=["all", "backgrounds", "characters"],
        default="all",
        help="Generate all images, only backgrounds, or only characters."
    )

    args = parser.parse_args()

    data = read_json(args.input)
    output_dir = Path(args.outdir)

    client = OpenAI(api_key=OPENAI_API_KEY)

    backgrounds = data.get("backgrounds", [])
    characters = data.get("characters", [])

    print("=" * 60)
    print(f"Input: {args.input}")
    print(f"Output dir: {output_dir}")
    print(f"Mode: {args.only}")
    print(f"Background count: {len(backgrounds)}")
    print(f"Character count: {len(characters)}")
    print("=" * 60)

    if args.only in ["all", "backgrounds"]:
        generate_backgrounds(client, backgrounds, output_dir)

    if args.only in ["all", "characters"]:
        generate_characters(client, characters, output_dir)

    print("=" * 60)
    print("Image generation finished.")
    print("=" * 60)


if __name__ == "__main__":
    main()