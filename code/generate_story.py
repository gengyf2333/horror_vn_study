# generate_neutral_story.py
# Generate a neutral, non-horror visual novel story package.
# Output is plain text, NOT JSON.

import argparse
from pathlib import Path
from openai import OpenAI

# =========================
# use your own api key and settings
# =========================

OPENAI_API_KEY = "sk-proj-xxxxxxxx"

MODEL_NAME = "gpt-5.5"

MAX_OUTPUT_TOKENS = 7000

DEFAULT_PROMPT_PATH = "prompt/neutral_novel.txt"
DEFAULT_OUTPUT_PATH = "outputs/story_neutral_text.txt"


def read_text(path: str) -> str:
    path_obj = Path(path)
    if not path_obj.exists():
        raise FileNotFoundError(f"Prompt file not found: {path}")
    return path_obj.read_text(encoding="utf-8")


def save_text(path: str, text: str) -> None:
    path_obj = Path(path)
    path_obj.parent.mkdir(parents=True, exist_ok=True)
    path_obj.write_text(text, encoding="utf-8")


def generate_neutral_story(
    prompt_path: str = DEFAULT_PROMPT_PATH,
    output_path: str = DEFAULT_OUTPUT_PATH,
    model: str = MODEL_NAME,
    max_output_tokens: int = MAX_OUTPUT_TOKENS,
):
    prompt = read_text(prompt_path)

    client = OpenAI(api_key=OPENAI_API_KEY)

    response = client.responses.create(
        model=model,
        input=[
            {
                "role": "developer",
                "content": (
                    "You are a careful visual novel story writer. "
                    "Follow the user's prompt exactly. "
                    "Do not generate JSON. "
                    "Do not generate image prompts. "
                    "Do not generate horror, suspense, mystery, or thriller content."
                ),
            },
            {
                "role": "user",
                "content": prompt,
            },
        ],
        max_output_tokens=max_output_tokens,
    )

    story_text = response.output_text.strip()
    save_text(output_path, story_text)

    print("=" * 60)
    print("Neutral story generated successfully.")
    print(f"Model used: {model}")
    print(f"Output saved to: {output_path}")
    print(f"Character count: {len(story_text)}")
    print("=" * 60)

    return story_text


def main():
    parser = argparse.ArgumentParser(
        description="Generate a neutral non-horror visual novel story package."
    )

    parser.add_argument(
        "--prompt",
        default=DEFAULT_PROMPT_PATH,
        help="Path to the neutral story prompt file.",
    )

    parser.add_argument(
        "--out",
        default=DEFAULT_OUTPUT_PATH,
        help="Output path for the generated story text.",
    )

    parser.add_argument(
        "--model",
        default=MODEL_NAME,
        help="Model name. This overrides MODEL_NAME in the script.",
    )

    parser.add_argument(
        "--max-output-tokens",
        type=int,
        default=MAX_OUTPUT_TOKENS,
        help="Maximum output tokens for the generated story.",
    )

    args = parser.parse_args()

    generate_neutral_story(
        prompt_path=args.prompt,
        output_path=args.out,
        model=args.model,
        max_output_tokens=args.max_output_tokens,
    )


if __name__ == "__main__":
    main()