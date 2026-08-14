const CLASS_NAMES = ["bite_indicator", "minigame_panel", "catch_zone", "moving_target"];
const COLORS = ["#ff5f57", "#4da3ff", "#35d07f", "#f7c948"];
const state = {
  summary: null,
  index: 0,
  frame: null,
  boxes: [],
  selected: -1,
  drawClass: 0,
  zoom: 1,
  fitZoom: 1,
  dirty: false,
  operation: null,
  saveQueue: Promise.resolve(),
  spaceDown: false,
};

const $ = (id) => document.getElementById(id);
const svg = $("overlay");
const canvas = $("canvas");
const viewport = $("viewport");

async function request(url, options = {}) {
  const response = await fetch(url, options);
  const payload = await response.json();
  if (!response.ok) throw new Error(payload.error || `HTTP ${response.status}`);
  return payload;
}

function apiName(name) { return encodeURIComponent(name); }
function setSaveState(text, error = false) {
  $("saveState").textContent = text;
  $("saveState").style.color = error ? "#ff8d8d" : "";
}

function buildClassControls() {
  const list = $("classList");
  const select = $("selectedClass");
  CLASS_NAMES.forEach((name, id) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "class-button";
    button.style.setProperty("--class-color", COLORS[id]);
    button.innerHTML = `<span class="swatch"></span><span class="class-id">${id}</span><span class="class-name">${name}</span>`;
    button.addEventListener("click", () => { state.drawClass = id; updateClassControls(); });
    list.appendChild(button);
    const option = document.createElement("option");
    option.value = id;
    option.textContent = `${id} ${name}`;
    select.appendChild(option);
  });
  updateClassControls();
}

function updateClassControls() {
  [...$("classList").children].forEach((el, id) => el.classList.toggle("active", id === state.drawClass));
  const hasSelection = state.selected >= 0 && state.selected < state.boxes.length;
  $("emptySelection").hidden = hasSelection;
  $("selectionControls").hidden = !hasSelection;
  if (hasSelection) $("selectedClass").value = state.boxes[state.selected].class_id;
}

function renderBoxList() {
  const list = $("boxList");
  list.replaceChildren();
  $("emptyBoxList").hidden = state.boxes.length > 0;
  state.boxes.forEach((box, index) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `box-node${index === state.selected ? " selected" : ""}`;
    button.style.setProperty("--box-color", COLORS[box.class_id]);
    button.setAttribute("aria-pressed", String(index === state.selected));
    button.setAttribute("aria-label", `选择框 ${index + 1} ${CLASS_NAMES[box.class_id]}`);

    const swatch = document.createElement("span");
    swatch.className = "swatch";
    const number = document.createElement("span");
    number.className = "box-number";
    number.textContent = `#${index + 1}`;
    const name = document.createElement("span");
    name.className = "box-name";
    name.textContent = CLASS_NAMES[box.class_id];
    const geometry = document.createElement("span");
    geometry.className = "box-geometry";
    geometry.textContent = `中心 ${Math.round(box.x_center * 100)}%, ${Math.round(box.y_center * 100)}% · 尺寸 ${Math.round(box.width * 100)}% × ${Math.round(box.height * 100)}%`;

    button.append(swatch, number, name, geometry);
    button.addEventListener("click", () => {
      state.selected = index;
      renderBoxes();
    });
    list.appendChild(button);
  });
}

function normalizedBox(box) {
  return {
    class_id: box.class_id,
    x_center: box.x_center,
    y_center: box.y_center,
    width: box.width,
    height: box.height,
  };
}

function toRect(box) {
  const width = state.frame.width;
  const height = state.frame.height;
  return {
    x: (box.x_center - box.width / 2) * width,
    y: (box.y_center - box.height / 2) * height,
    width: box.width * width,
    height: box.height * height,
  };
}

function fromRect(rect, classId) {
  const width = state.frame.width;
  const height = state.frame.height;
  const x1 = Math.max(0, Math.min(width, rect.x));
  const y1 = Math.max(0, Math.min(height, rect.y));
  const x2 = Math.max(0, Math.min(width, rect.x + rect.width));
  const y2 = Math.max(0, Math.min(height, rect.y + rect.height));
  return {
    class_id: classId,
    x_center: ((x1 + x2) / 2) / width,
    y_center: ((y1 + y2) / 2) / height,
    width: (x2 - x1) / width,
    height: (y2 - y1) / height,
  };
}

function svgElement(name, attrs = {}) {
  const el = document.createElementNS("http://www.w3.org/2000/svg", name);
  Object.entries(attrs).forEach(([key, value]) => el.setAttribute(key, value));
  return el;
}

function renderBoxes() {
  svg.replaceChildren();
  if (!state.frame) return;
  state.boxes.forEach((box, index) => {
    const rect = toRect(box);
    const color = COLORS[box.class_id];
    const group = svgElement("g", {"data-index": index});
    const boxRect = svgElement("rect", {
      x: rect.x, y: rect.y, width: rect.width, height: rect.height,
      fill: "none",
      stroke: color, class: `box${index === state.selected ? " selected" : ""}`,
      "data-role": "box", "data-index": index,
    });
    group.appendChild(boxRect);
    if (index === state.selected) {
      const points = {
        nw: [rect.x, rect.y], ne: [rect.x + rect.width, rect.y],
        sw: [rect.x, rect.y + rect.height], se: [rect.x + rect.width, rect.y + rect.height],
      };
      Object.entries(points).forEach(([corner, point]) => {
        group.appendChild(svgElement("rect", {
          x: point[0] - 7, y: point[1] - 7, width: 14, height: 14,
          class: "handle", "data-role": "handle", "data-index": index, "data-corner": corner,
        }));
      });
    }
    svg.appendChild(group);
  });
  renderBoxList();
  updateClassControls();
  updateValidation();
}

function localPoint(event) {
  const point = svg.createSVGPoint();
  point.x = event.clientX;
  point.y = event.clientY;
  const transformed = point.matrixTransform(svg.getScreenCTM().inverse());
  return {x: transformed.x, y: transformed.y};
}

function beginPointer(event) {
  if (!state.frame) return;
  if (event.button === 1 || (event.button === 0 && state.spaceDown)) {
    event.preventDefault();
    state.operation = {type: "pan", x: event.clientX, y: event.clientY, left: viewport.scrollLeft, top: viewport.scrollTop};
    svg.setPointerCapture(event.pointerId);
    svg.style.cursor = "grabbing";
    return;
  }
  if (event.button !== 0) return;
  event.preventDefault();
  const point = localPoint(event);
  const role = event.target.dataset.role;
  const index = Number(event.target.dataset.index);
  if (role === "handle") {
    state.selected = index;
    state.operation = {type: "resize", index, corner: event.target.dataset.corner, start: point, rect: toRect(state.boxes[index])};
  } else if (role === "box") {
    state.selected = index;
    state.operation = {type: "move", index, start: point, rect: toRect(state.boxes[index])};
  } else {
    state.selected = state.boxes.length;
    state.boxes.push(fromRect({x: point.x, y: point.y, width: 0.001, height: 0.001}, state.drawClass));
    state.operation = {type: "draw", index: state.selected, start: point};
  }
  svg.setPointerCapture(event.pointerId);
  renderBoxes();
}

function movePointer(event) {
  const op = state.operation;
  if (!op) return;
  if (op.type === "pan") {
    viewport.scrollLeft = op.left - (event.clientX - op.x);
    viewport.scrollTop = op.top - (event.clientY - op.y);
    return;
  }
  const point = localPoint(event);
  let rect;
  if (op.type === "draw") {
    rect = {x: Math.min(op.start.x, point.x), y: Math.min(op.start.y, point.y), width: Math.abs(point.x - op.start.x), height: Math.abs(point.y - op.start.y)};
  } else if (op.type === "move") {
    rect = {...op.rect, x: op.rect.x + point.x - op.start.x, y: op.rect.y + point.y - op.start.y};
    rect.x = Math.max(0, Math.min(state.frame.width - rect.width, rect.x));
    rect.y = Math.max(0, Math.min(state.frame.height - rect.height, rect.y));
  } else {
    const x1 = op.corner.includes("w") ? point.x : op.rect.x;
    const x2 = op.corner.includes("e") ? point.x : op.rect.x + op.rect.width;
    const y1 = op.corner.includes("n") ? point.y : op.rect.y;
    const y2 = op.corner.includes("s") ? point.y : op.rect.y + op.rect.height;
    rect = {x: Math.min(x1, x2), y: Math.min(y1, y2), width: Math.abs(x2 - x1), height: Math.abs(y2 - y1)};
  }
  state.boxes[op.index] = fromRect(rect, state.boxes[op.index].class_id);
  state.dirty = true;
  renderBoxes();
}

async function endPointer(event) {
  if (!state.operation) return;
  if (state.operation.type === "pan") {
    state.operation = null;
    svg.style.cursor = "crosshair";
    return;
  }
  const index = state.operation.index;
  state.operation = null;
  if (state.boxes[index].width * state.frame.width < 3 || state.boxes[index].height * state.frame.height < 3) {
    state.boxes.splice(index, 1);
    state.selected = -1;
  }
  renderBoxes();
  await saveDraft();
}

function validationErrors() {
  const counts = [0, 0, 0, 0];
  state.boxes.forEach(box => counts[box.class_id]++);
  const errors = [];
  counts.forEach((count, id) => { if (count > 1) errors.push(`${CLASS_NAMES[id]} 只能有一个框`); });
  if ((counts[2] || counts[3]) && counts[1] !== 1) errors.push("小游戏目标必须有一个 minigame_panel");
  if (counts[1] && (counts[2] !== 1 || counts[3] !== 1)) errors.push("小游戏画面必须同时有 catch_zone 和 moving_target");
  return errors;
}

function updateValidation() {
  const errors = validationErrors();
  $("validation").hidden = errors.length === 0;
  $("validation").textContent = errors.join("；");
}

async function saveDraft(reviewed = false) {
  if (!state.frame) return;
  const filename = state.frame.filename;
  const labels = state.boxes.map(normalizedBox);
  setSaveState("保存中");
  const operation = async () => request(`/api/frame/${apiName(filename)}`, {
    method: "PUT", headers: {"Content-Type": "application/json"},
    body: JSON.stringify({labels, reviewed}),
  });
  state.saveQueue = state.saveQueue.catch(() => {}).then(operation);
  try {
    const result = await state.saveQueue;
    if (state.frame && state.frame.filename === filename) {
      const changedReviewState = state.frame.reviewed !== result.reviewed;
      state.frame.reviewed = result.reviewed;
      state.dirty = false;
      updateReviewBadge();
      if (changedReviewState) await refreshSummary();
      setSaveState("已保存");
    }
    return result;
  } catch (error) {
    setSaveState("保存失败", true);
    throw error;
  }
}

async function refreshSummary() {
  state.summary = await request("/api/summary");
  $("recording").textContent = state.summary.recording;
  $("reviewedCount").textContent = state.summary.reviewed;
  $("positiveCount").textContent = state.summary.positive;
  $("negativeCount").textContent = state.summary.negative;
  $("remainingCount").textContent = state.summary.remaining;
  $("progressText").textContent = `${state.summary.reviewed} / ${state.summary.total}`;
  $("progressBar").style.width = `${state.summary.total ? state.summary.reviewed / state.summary.total * 100 : 0}%`;
  $("frameSlider").max = Math.max(1, state.summary.total);
}

async function loadFrame(index) {
  if (state.dirty) await saveDraft(false);
  index = Math.max(0, Math.min(state.summary.frames.length - 1, index));
  state.index = index;
  state.frame = await request(`/api/frame/${apiName(state.summary.frames[index])}`);
  state.boxes = state.frame.labels.map(normalizedBox);
  state.selected = -1;
  state.dirty = false;
  $("frameImage").src = `/frames/${apiName(state.frame.filename)}`;
  svg.setAttribute("viewBox", `0 0 ${state.frame.width} ${state.frame.height}`);
  $("frameCounter").textContent = `${index + 1} / ${state.summary.total}`;
  $("frameSlider").value = index + 1;
  $("frameSliderValue").textContent = index + 1;
  $("frameName").textContent = state.frame.filename;
  $("previous").disabled = index === 0;
  $("next").disabled = index === state.summary.total - 1;
  updateReviewBadge();
  fitCanvas();
  renderBoxes();
  setSaveState("就绪");
}

function updateReviewBadge() {
  const badge = $("reviewBadge");
  badge.textContent = state.frame.reviewed ? "已审核" : "待审核";
  badge.className = `badge ${state.frame.reviewed ? "reviewed" : "pending"}`;
}

function applyZoom(zoom) {
  state.zoom = Math.max(0.08, Math.min(3, zoom));
  canvas.style.width = `${Math.round(state.frame.width * state.zoom)}px`;
  canvas.style.height = `${Math.round(state.frame.height * state.zoom)}px`;
  $("zoomValue").textContent = `${Math.round(state.zoom * 100)}%`;
}

function fitCanvas() {
  if (!state.frame) return;
  const availableWidth = Math.max(100, viewport.clientWidth - 36);
  const availableHeight = Math.max(100, viewport.clientHeight - 36);
  state.fitZoom = Math.min(availableWidth / state.frame.width, availableHeight / state.frame.height, 1);
  applyZoom(state.fitZoom);
  viewport.scrollTo(0, 0);
}

async function confirmCurrent(forceNegative = false) {
  if (forceNegative) {
    state.boxes = [];
    state.selected = -1;
    state.dirty = true;
    renderBoxes();
  }
  const errors = validationErrors();
  if (errors.length) { $("validation").hidden = false; return; }
  try {
    await saveDraft(true);
    state.frame.reviewed = true;
    updateReviewBadge();
    await refreshSummary();
    if (state.index < state.summary.total - 1) await loadFrame(state.index + 1);
  } catch (error) {
    $("validation").hidden = false;
    $("validation").textContent = error.message;
  }
}

async function resetCurrent() {
  try {
    const result = await request(`/api/frame/${apiName(state.frame.filename)}`, {method: "DELETE"});
    state.frame = result;
    state.boxes = result.labels.map(normalizedBox);
    state.selected = -1;
    state.dirty = false;
    updateReviewBadge();
    renderBoxes();
    await refreshSummary();
    setSaveState("已恢复");
  } catch (error) { setSaveState("恢复失败", true); }
}

function deleteSelected() {
  if (state.selected < 0) return;
  state.boxes.splice(state.selected, 1);
  state.selected = -1;
  state.dirty = true;
  renderBoxes();
  saveDraft(false).catch(() => {});
}

async function initialize() {
  buildClassControls();
  await refreshSummary();
  await loadFrame(0);
}

svg.addEventListener("pointerdown", beginPointer);
svg.addEventListener("pointermove", movePointer);
svg.addEventListener("pointerup", endPointer);
svg.addEventListener("pointercancel", endPointer);
$("previous").addEventListener("click", () => loadFrame(state.index - 1));
$("next").addEventListener("click", () => loadFrame(state.index + 1));
$("frameSlider").addEventListener("input", event => {
  $("frameSliderValue").textContent = event.target.value;
});
$("frameSlider").addEventListener("change", event => {
  loadFrame(Number(event.target.value) - 1).catch(error => setSaveState(error.message, true));
});
$("confirm").addEventListener("click", () => confirmCurrent(false));
$("negative").addEventListener("click", () => confirmCurrent(true));
$("reset").addEventListener("click", resetCurrent);
$("deleteBox").addEventListener("click", deleteSelected);
$("selectedClass").addEventListener("change", event => {
  if (state.selected < 0) return;
  state.boxes[state.selected].class_id = Number(event.target.value);
  state.dirty = true;
  renderBoxes();
  saveDraft(false).catch(() => {});
});
$("zoomIn").addEventListener("click", () => applyZoom(state.zoom * 1.2));
$("zoomOut").addEventListener("click", () => applyZoom(state.zoom / 1.2));
$("fit").addEventListener("click", fitCanvas);
viewport.addEventListener("wheel", event => {
  if (!state.frame) return;
  event.preventDefault();
  const before = state.zoom;
  const rect = canvas.getBoundingClientRect();
  const imageX = (event.clientX - rect.left) / before;
  const imageY = (event.clientY - rect.top) / before;
  applyZoom(event.deltaY < 0 ? before * 1.12 : before / 1.12);
  const nextRect = canvas.getBoundingClientRect();
  viewport.scrollLeft += nextRect.left + imageX * state.zoom - event.clientX;
  viewport.scrollTop += nextRect.top + imageY * state.zoom - event.clientY;
}, {passive: false});
window.addEventListener("keydown", event => {
  if (["INPUT", "SELECT", "TEXTAREA"].includes(document.activeElement.tagName)) return;
  if (event.key === "ArrowLeft" || event.key.toLowerCase() === "a") loadFrame(state.index - 1);
  if (event.key === "ArrowRight" || event.key.toLowerCase() === "d") loadFrame(state.index + 1);
  if (event.key === "Delete") deleteSelected();
  if (["1", "2", "3", "4"].includes(event.key)) { state.drawClass = Number(event.key) - 1; updateClassControls(); }
  if (event.code === "Space") { state.spaceDown = true; event.preventDefault(); }
});
window.addEventListener("keyup", event => { if (event.code === "Space") state.spaceDown = false; });
window.addEventListener("resize", () => { if (Math.abs(state.zoom - state.fitZoom) < 0.001) fitCanvas(); });
initialize().catch(error => { setSaveState("启动失败", true); $("validation").hidden = false; $("validation").textContent = error.message; });
