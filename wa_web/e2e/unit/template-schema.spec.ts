import { test, expect } from "@playwright/test";
import {
  builderSchema,
  buildTemplatePayload,
  emptyBuilderValues,
  emptyCard,
  templateToFormValues,
  type BuilderFormValues,
} from "@/features/templates/schema/template-schema";
import type { Template } from "@/features/templates/types";

// Pure schema/payload tests — no browser, no API. Run with: npx playwright test --project=unit

/** A valid 2-card image carousel with one quick-reply slot per card. */
function carousel(overrides: Partial<BuilderFormValues> = {}): BuilderFormValues {
  const card = () => ({
    ...emptyCard(["quick_reply"]),
    mediaHandle: "h:abc",
    mediaPreviewId: "prev-1",
    buttons: [{ text: "Shop now", url: "", phoneNumber: "" }],
  });
  return {
    ...emptyBuilderValues(),
    wabaConnectionId: "c1",
    name: "spring_cards",
    mode: "carousel",
    body: "Check out our new arrivals this spring",
    buttons: [],
    cardMediaType: "image",
    cardButtonTypes: ["quick_reply"],
    cards: [card(), card()],
    ...overrides,
  };
}

function messages(v: BuilderFormValues) {
  const r = builderSchema.safeParse(v);
  return r.success ? [] : r.error.issues.map((i) => `${i.path.join(".")}: ${i.message}`);
}

test.describe("carousel schema", () => {
  test("a valid carousel passes", () => {
    expect(messages(carousel())).toEqual([]);
  });

  test("header, footer and top-level button rules are skipped in carousel mode", () => {
    const v = carousel({ headerType: "image", headerMediaHandle: "", footerEnabled: true, footer: "" });
    expect(messages(v)).toEqual([]);
  });

  test("requires at least 2 and at most 10 cards", () => {
    const one = carousel();
    expect(messages({ ...one, cards: one.cards.slice(0, 1) }).join()).toContain("at least 2 cards");
    const eleven = carousel({ cards: Array.from({ length: 11 }, () => carousel().cards[0]) });
    expect(messages(eleven).join()).toContain("At most 10 cards");
  });

  test("every card needs uploaded media", () => {
    const v = carousel();
    v.cards[1] = { ...v.cards[1], mediaHandle: "", mediaPreviewId: "" };
    expect(messages(v)).toEqual(["cards.1.mediaHandle: Upload a sample image"]);
  });

  test("card text: max 160, no variables, all-or-nothing", () => {
    const tooLong = carousel();
    tooLong.cards[0].text = "x".repeat(161);
    tooLong.cards[1].text = "ok";
    expect(messages(tooLong).join()).toContain("cards.0.text: Max 160 characters");

    const variable = carousel();
    variable.cards[0].text = "Hi {{1}}";
    variable.cards[1].text = "ok";
    expect(messages(variable).join()).toContain("cards.0.text: Variables like {{1}}");

    const partial = carousel();
    partial.cards[0].text = "Only the first has text";
    expect(messages(partial).join()).toContain("cards.1.text: Add text");

    const none = carousel();
    expect(messages(none)).toEqual([]);
  });

  test("needs 1-2 layout buttons; label max 25 chars and required", () => {
    expect(messages(carousel({ cardButtonTypes: [] })).join()).toContain("cardButtonTypes: Every card needs 1 or 2 buttons");

    const three = carousel();
    expect(builderSchema.safeParse({ ...three, cardButtonTypes: ["quick_reply", "url", "phone_number"] }).success).toBe(false);

    const long = carousel();
    long.cards[0].buttons[0].text = "x".repeat(26);
    expect(messages(long).join()).toContain("Max 25 characters");

    const blank = carousel();
    blank.cards[0].buttons[0].text = "";
    expect(messages(blank).join()).toContain("cards.0.buttons.0.text: Button text is required");
  });

  test("url buttons must be static; phone uses E.164", () => {
    const url = carousel({ cardButtonTypes: ["url"] });
    url.cards.forEach((c) => (c.buttons = [{ text: "Open", url: "https://x.com/a", phoneNumber: "" }]));
    expect(messages(url)).toEqual([]);
    url.cards[0].buttons[0].url = "https://x.com/{{1}}";
    expect(messages(url).join()).toContain("cards.0.buttons.0.url: Card URLs must be static");
    url.cards[0].buttons[0].url = "";
    expect(messages(url).join()).toContain("cards.0.buttons.0.url: URL is required");

    const phone = carousel({ cardButtonTypes: ["phone_number"] });
    phone.cards.forEach((c) => (c.buttons = [{ text: "Call", url: "", phoneNumber: "+94 77 123 4567" }]));
    expect(messages(phone)).toEqual([]);
    phone.cards[1].buttons[0].phoneNumber = "0771234567";
    expect(messages(phone).join()).toContain("cards.1.buttons.0.phoneNumber");
  });

  test("top-level body rules (variables need examples) still apply", () => {
    const v = carousel({ body: "Hi {{1}}, check out our spring arrivals today", bodyExamples: [{ value: "" }] });
    expect(messages(v).join()).toContain("bodyExamples.0.value: Example required");
  });

  test("standard mode ignores card data", () => {
    const v = { ...emptyBuilderValues(), wabaConnectionId: "c1", name: "plain", body: "Hello there friend", cards: [] };
    expect(messages(v)).toEqual([]);
  });
});

test.describe("carousel payload + round trip", () => {
  test("builds the contract: null header/footer, empty buttons, cards", () => {
    const v = carousel({ cardButtonTypes: ["url", "phone_number"], cardMediaType: "video" });
    v.cards.forEach((c, i) => {
      c.text = `Card ${i}`;
      c.buttons = [
        { text: "Open", url: "https://x.com", phoneNumber: "" },
        { text: "Call", url: "", phoneNumber: "+94 77 123 4567" },
      ];
    });
    const { components } = buildTemplatePayload(v);
    expect(components.header).toBeNull();
    expect(components.footer).toBeNull();
    expect(components.buttons).toEqual([]);
    expect(components.carousel?.cards).toHaveLength(2);
    const c0 = components.carousel!.cards[0];
    expect(c0.header).toEqual({ type: "video", mediaHandle: "h:abc", mediaPreviewId: "prev-1" });
    expect(c0.body).toEqual({ text: "Card 0", examples: [] });
    expect(c0.buttons.map((b) => b.type)).toEqual(["url", "phone_number"]);
    expect(c0.buttons[1].phoneNumber).toBe("+94771234567");
  });

  test("standard mode sends carousel: null", () => {
    const { components } = buildTemplatePayload({ ...emptyBuilderValues(), body: "Hello there friend" });
    expect(components.carousel).toBeNull();
    expect(components.buttons).toHaveLength(1); // seeded Stop button
  });

  test("a saved carousel loads back into the builder", () => {
    const v = carousel({ cardButtonTypes: ["url"] });
    v.cards.forEach((c, i) => {
      c.text = `Card ${i}`;
      c.buttons = [{ text: "Open", url: `https://x.com/${i}`, phoneNumber: "" }];
    });
    const payload = buildTemplatePayload(v);
    const template = {
      id: "t1",
      wabaId: "w",
      status: "Draft",
      isActive: true,
      createdAt: "",
      parameterFormat: "Positional",
      category: "Marketing",
      ...payload,
    } as Template;
    const back = templateToFormValues(template);
    expect(back.mode).toBe("carousel");
    expect(back.cardMediaType).toBe("image");
    expect(back.cardButtonTypes).toEqual(["url"]);
    expect(back.cards.map((c) => c.buttons[0].url)).toEqual(["https://x.com/0", "https://x.com/1"]);
    expect(messages(back)).toEqual([]);
  });
});
