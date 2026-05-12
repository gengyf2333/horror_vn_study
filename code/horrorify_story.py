# horrorify_story.py
# Read a neutral story text file and generate a horrorified version.
# Output is plain text, NOT JSON.

import argparse
from pathlib import Path
from openai import OpenAI


# =========================
# use your own api key and settings
# =========================

OPENAI_API_KEY = "sk-proj-xxxxxxx"

MODEL_NAME = "gpt-5.5"

MAX_OUTPUT_TOKENS = 9000

DEFAULT_PROMPT_PATH = "prompt/horror_novel.txt"
DEFAULT_INPUT_STORY_PATH = "outputs/story_neutral_text.txt"
DEFAULT_OUTPUT_PATH = "outputs/story_horrorified_text.txt"


def read_text(path: str) -> str:
    path_obj = Path(path)
    if not path_obj.exists():
        raise FileNotFoundError(f"File not found: {path}")
    return path_obj.read_text(encoding="utf-8")


def save_text(path: str, text: str) -> None:
    path_obj = Path(path)
    path_obj.parent.mkdir(parents=True, exist_ok=True)
    path_obj.write_text(text, encoding="utf-8")


def horrorify_story(
    prompt_path: str = DEFAULT_PROMPT_PATH,
    input_story_path: str = DEFAULT_INPUT_STORY_PATH,
    output_path: str = DEFAULT_OUTPUT_PATH,
    model: str = MODEL_NAME,
    max_output_tokens: int = MAX_OUTPUT_TOKENS,
):
    instruction_prompt = read_text(prompt_path)
    neutral_story_text = read_text(input_story_path)

    client = OpenAI(api_key=OPENAI_API_KEY)

    response = client.responses.create(
        model=model,
        input=[
            {
                "role": "developer",
                "content": (
                    "You are a careful horror visual novel narrative editor. "
                    "Follow the user's instruction exactly. "
                    "Preserve story structure, character consistency, and scene order. "
                    "Do not output JSON. "
                    "Do not generate image prompts."
                ),
            },
            {
                "role": "user",
                "content": instruction_prompt,
            },
            {
                "role": "user",
                "content": (
                    "Here is the neutral visual novel story text to horrorify:\n\n"
                    "----- BEGIN NEUTRAL STORY -----\n"
                    f"{neutral_story_text}\n"
                    "----- END NEUTRAL STORY -----"
                ),
            },
        ],
        max_output_tokens=max_output_tokens,
    )

    horrorified_text = response.output_text.strip()
    save_text(output_path, horrorified_text)

    print("=" * 60)
    print("Horrorified story generated successfully.")
    print(f"Model used: {model}")
    print(f"Input story: {input_story_path}")
    print(f"Output saved to: {output_path}")
    print(f"Output character count: {len(horrorified_text)}")
    print("=" * 60)

    return horrorified_text


def main():
    parser = argparse.ArgumentParser(
        description="Generate a horrorified version from a neutral visual novel story text."
    )

    parser.add_argument(
        "--prompt",
        default=DEFAULT_PROMPT_PATH,
        help="Path to the horrorification prompt file.",
    )

    parser.add_argument(
        "--input",
        default=DEFAULT_INPUT_STORY_PATH,
        help="Path to the neutral story text file.",
    )

    parser.add_argument(
        "--out",
        default=DEFAULT_OUTPUT_PATH,
        help="Output path for the horrorified story text.",
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
        help="Maximum output tokens for the generated horrorified story.",
    )

    args = parser.parse_args()

    horrorify_story(
        prompt_path=args.prompt,
        input_story_path=args.input,
        output_path=args.out,
        model=args.model,
        max_output_tokens=args.max_output_tokens,
    )


if __name__ == "__main__":
    main()