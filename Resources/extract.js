/*
 * Runs inside the logged-in Amazon "Your Orders" page and returns a JSON array
 * of shipments. Amazon scrambles its CSS class names and reshuffles markup often,
 * so this leans on STABLE signals — order links, visible status text, image alt
 * text — rather than brittle class selectors. When Amazon changes things, this is
 * the file to tune; it ships next to the exe and is reloaded each run.
 *
 * Return shape (array):
 *   { orderId, title, statusText, etaText, stopsAway, orderUrl }
 */
(function () {
  "use strict";

  const origin = location.origin;

  // Pull an order id out of any of the URL shapes Amazon uses.
  function orderIdFrom(href) {
    if (!href) return null;
    const m = href.match(/(?:orderID|orderId)=([0-9-]{10,})/i)
           || href.match(/\/(?:gp\/css\/order-details|your-orders\/order-details)\b[^]*?([0-9]{3}-[0-9]{7}-[0-9]{7})/);
    return m ? m[1] : (href.match(/([0-9]{3}-[0-9]{7}-[0-9]{7})/) || [])[1] || null;
  }

  // The repeating order "card" containers. Try a few known hooks, then fall back
  // to: any element that contains an order-id link and a chunk of status text.
  function findCards() {
    const known = document.querySelectorAll(
      ".order-card, .js-order-card, [class*='order-card'], li.order, .a-box-group.order"
    );
    if (known.length) return Array.from(known);

    // Fallback: climb up from each order link to a reasonable card-sized ancestor.
    const seen = new Set();
    const cards = [];
    document.querySelectorAll("a[href*='order-details'], a[href*='orderID=']").forEach((a) => {
      let el = a;
      for (let i = 0; i < 6 && el; i++) el = el.parentElement;
      if (el && !seen.has(el)) { seen.add(el); cards.push(el); }
    });
    return cards;
  }

  function textOf(el) {
    return (el ? el.textContent : "").replace(/\s+/g, " ").trim();
  }

  // Text content with <script>/<style>/<noscript> removed — Amazon embeds inline
  // scripts (with code comments!) inside each order card, and we must not read those
  // as if they were visible order text.
  function cleanText(el) {
    if (!el) return "";
    const clone = el.cloneNode(true);
    clone.querySelectorAll("script, style, noscript").forEach((n) => n.remove());
    return (clone.textContent || "").replace(/\s+/g, " ").trim();
  }

  // The "Arriving today / tomorrow / Mon Jun 22" delivery estimate, time range stripped.
  function arrivingFrom(cardText) {
    const m = cardText.match(/\b(?:now\s+)?arriving\s+(today|tomorrow)\b/i)
           || cardText.match(/\b(?:now\s+)?arriving\s+[A-Za-z]{3,9}\.?\s+\d{1,2}(?:\s*-\s*[A-Za-z]{3,9}\.?\s+\d{1,2})?/i);
    return m ? m[0].replace(/\bnow\s+/i, "").replace(/\s+/g, " ").trim() : "";
  }

  // The delivery status. Amazon puts it in a dedicated node on normal cards; that's the
  // source of truth. The featured "Arriving today" hero card has no such node, so we
  // read its header / progress tracker instead. Text scan is the last resort.
  function statusFrom(card, cardText) {
    const node = card.querySelector(
      ".yohtmlc-shipment-status-primaryText, [class*='shipment-status-primaryText'], [class*='shipment-status']"
    );
    let cand = node ? cleanText(node) : "";

    // Hero card: prefer the "Arriving ..." header (carries the date), else "Out for delivery".
    if (!cand) {
      cand = arrivingFrom(cardText);
      if (!cand && /\bout for delivery\b/i.test(cardText)) cand = "Out for delivery";
    }

    // Last resort: scan for a short status token (bounded, so we don't grab tracker labels).
    if (!cand) {
      const patterns = [
        /\b(?:order\s+|item\s+)?cancell?ed\b/i,
        /\breturn (?:received|started|completed)\b/i,
        /\bdelivered\b(?:\s+[A-Za-z]{3,9}\.?\s+\d{1,2})?/i,
        /\bout for delivery\b/i,
        /\bshipped\b/i,
        /\bpreparing for shipment\b/i,
        /\bnot yet shipped\b/i,
      ];
      for (const re of patterns) {
        const m = cardText.match(re);
        if (m) { cand = m[0].trim(); break; }
      }
    }

    // Drop any time range ("12:15 PM - 3:15 PM") so it reads as a clean status.
    cand = cand.replace(/\s*\d{1,2}:\d{2}\s*[AP]M(\s*-\s*\d{1,2}:\d{2}\s*[AP]M)?/gi, "").trim();

    // Safety net: a real status is short; reject long or code-like text.
    if (!cand || cand.length > 40 || /[{}]|\/\/|=>|function|\bnode\b/i.test(cand)) return "";
    return cand;
  }

  function etaFrom(cardText) {
    const m = cardText.match(/\b(?:today|tomorrow)\b(?:\s+by\s+[^,.]+)?/i)
          || cardText.match(/\b(?:by\s+)?[A-Z][a-z]{2,8}\.?\s+\d{1,2}(?:,\s*\d{4})?/);
    return m ? m[0].trim() : "";
  }

  function stopsFrom(cardText) {
    const m = cardText.match(/(\d+)\s+stops?\s+away/i);
    return m ? parseInt(m[1], 10) : null;
  }

  // The delivery time window, e.g. "12:15 PM - 3:15 PM" or "by 9 PM".
  function windowFrom(cardText) {
    const m = cardText.match(/\b\d{1,2}(?::\d{2})?\s*[AP]M\s*-\s*\d{1,2}(?::\d{2})?\s*[AP]M/i)
           || cardText.match(/\bby\s+\d{1,2}(?::\d{2})?\s*[AP]M\b/i);
    return m ? m[0].replace(/\s+/g, " ").trim() : "";
  }

  // The product thumbnail. Prefer an image inside a product link; fall back to the
  // first Amazon-CDN image, then any image.
  function imageFrom(card) {
    const img = card.querySelector("a[href*='/dp/'] img")
             || card.querySelector("a[href*='/product/'] img")
             || card.querySelector("img[src*='media-amazon']")
             || card.querySelector("img[src]");
    let src = img ? (img.getAttribute("src") || "") : "";
    if (src.startsWith("//")) src = "https:" + src;
    return src.startsWith("http") ? src : "";
  }

  // "Tracking ID: TBA331903997192" / "Tracking number 1Z..." etc.
  function trackingFrom(cardText) {
    const m = cardText.match(/tracking\s*(?:id|number|no\.?|#)\s*:?\s*([A-Z0-9]{8,40})/i);
    return m ? m[1].toUpperCase() : "";
  }

  // "Shipped with Amazon" / "Carrier: UPS"
  function carrierFrom(cardText) {
    const m = cardText.match(/shipped\s+with\s+([A-Za-z][A-Za-z.&-]{1,18})/i)
           || cardText.match(/carrier\s*:?\s*([A-Za-z][A-Za-z.&-]{1,18})/i);
    return m ? m[1].replace(/[.&-]+$/, "").trim() : "";
  }

  function titleFrom(card) {
    // Prefer product link text; fall back to the first product image's alt text.
    const link = card.querySelector("a[href*='/dp/'], a[href*='/gp/product/'], a.a-link-normal[href*='/product/']");
    let t = textOf(link);
    if (!t) {
      const img = card.querySelector("img[alt]");
      t = img ? img.getAttribute("alt").trim() : "";
    }
    if (t.length > 90) t = t.slice(0, 87) + "…";
    return t || "(item)";
  }

  const STATUS_SEL = ".yohtmlc-shipment-status-primaryText, [class*='shipment-status-primaryText']";

  // "Now arriving <future date>" = Amazon pushed the estimate back (delayed/stuck).
  // "Now arriving today/tomorrow" is imminent, not delayed.
  function isDelayed(status) {
    return /\bnow arriving\b/i.test(status) && !/\b(today|tomorrow)\b/i.test(status);
  }

  // An order card can hold several shipments (e.g. one item delivered, another delayed).
  // When there are 2+ status nodes, split into one shipment each, pairing every status
  // with the next product item that follows it in document order. ("Now arriving" marks
  // a pushed-back / delayed shipment.) Single-shipment cards return [] -> caller uses the
  // existing whole-card logic.
  function splitShipments(card) {
    const nodes = card.querySelectorAll(STATUS_SEL);
    if (nodes.length <= 1) return [];

    const ships = [];
    let cur = null;
    const all = card.querySelectorAll("*");
    for (let i = 0; i < all.length; i++) {
      const el = all[i];
      if (el.matches(STATUS_SEL)) {
        const s = cleanText(el);
        if (!s) continue;
        cur = { status: s, title: "", img: "", delayed: isDelayed(s) };
        ships.push(cur);
        continue;
      }
      if (!cur) continue;
      if (!cur.title && el.matches && el.matches("a[href*='/dp/'], a[href*='/gp/product/'], a[href*='/product/']")) {
        let t = textOf(el);
        if (t) cur.title = t.length > 90 ? t.slice(0, 87) + "…" : t;
      }
      if (!cur.img && el.tagName === "IMG") {
        let src = el.getAttribute("src") || "";
        if (src.startsWith("//")) src = "https:" + src;
        if (src.startsWith("http") && src.indexOf("media-amazon") >= 0) cur.img = src;
      }
    }
    return ships.filter((s) => s.status);
  }

  const results = [];
  const seenKeys = new Set();
  const cards = findCards();

  function addRow(orderId, orderUrl, cardText, statusText, title, imageUrl, delayed) {
    const key = orderId + "|" + title;
    if (seenKeys.has(key)) return;
    seenKeys.add(key);
    results.push({
      orderId: orderId,
      title: title,
      statusText: statusText,
      etaText: etaFrom(statusText),
      stopsAway: stopsFrom(cardText),
      deliveryWindow: windowFrom(statusText) || windowFrom(cardText),
      orderUrl: orderUrl,
      imageUrl: imageUrl,
      trackingId: trackingFrom(cardText),
      carrier: carrierFrom(cardText),
      delayed: !!delayed,
      rawText: cardText.slice(0, 700), // diagnostics only (written to the debug-gated dump)
    });
  }

  cards.forEach((card) => {
    const link = card.querySelector("a[href*='order-details'], a[href*='orderID=']");
    const href = link ? link.getAttribute("href") : null;
    const orderId = orderIdFrom(href) || orderIdFrom(card.innerHTML) || "";
    if (!orderId) return;

    const cardText = cleanText(card);
    const orderUrl = href
      ? (href.startsWith("http") ? href : origin + href)
      : origin + "/gp/css/order-details?orderID=" + orderId;

    const ships = splitShipments(card);
    if (ships.length === 0) {
      // Single shipment (or none): pull the ETA from the status, not the whole card.
      const statusText = statusFrom(card, cardText);
      addRow(orderId, orderUrl, cardText, statusText, titleFrom(card), imageFrom(card), isDelayed(statusText));
    } else {
      ships.forEach((sh) =>
        addRow(orderId, orderUrl, cardText, sh.status, sh.title || titleFrom(card), sh.img || imageFrom(card), sh.delayed));
    }
  });

  // The "Next" pagination link, if there is one. On the last page Amazon renders
  // this as a disabled <span> (no <a>), so the query returns null and we stop.
  let nextEl = document.querySelector("ul.a-pagination li.a-last a")
            || document.querySelector(".a-pagination .a-last a")
            || document.querySelector("a[aria-label='Next'], a[aria-label='Go to next page']");
  let nextUrl = nextEl ? (nextEl.getAttribute("href") || "") : "";
  if (nextUrl.startsWith("/")) nextUrl = origin + nextUrl;
  if (nextUrl && !nextUrl.startsWith("http")) nextUrl = "";

  // One-shot diagnostic: what the page actually is, and the raw text of the first
  // couple of cards, so the status wording / location can be tuned to real markup.
  const debug = {
    url: location.href,
    title: document.title,
    cardCount: cards.length,
    sample0: cards.length > 0 ? textOf(cards[0]).slice(0, 1200) : "(no cards)",
    sample1: cards.length > 1 ? textOf(cards[1]).slice(0, 1200) : ""
  };

  return JSON.stringify({ orders: results, nextUrl: nextUrl, debug: debug });
})();
