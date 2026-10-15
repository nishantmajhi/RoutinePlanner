document.addEventListener("DOMContentLoaded", () => {
  const availabilitySection = document.querySelector(".availability");
  availabilitySection.innerHTML = "";

  const days = [
    "Sunday",
    "Monday",
    "Tuesday",
    "Wednesday",
    "Thursday",
    "Friday",
    "Saturday",
  ];

  days.forEach((day) => {
    const dayLabel = createElement("label");
    const dayName = createElement("h5", { class: "day" }, day);
    dayLabel.appendChild(dayName);

    const slotsContainer = createElement("div", { class: "slots_container" });
    const addSlotButton = createElement(
      "button",
      { type: "button", title: "Add new timeslot" },
      "➕"
    );

    addSlotButton.addEventListener("click", () => {
      const isEmpty = slotsContainer.querySelector(".slot.empty");
      if (!isEmpty) {
        const newSlot = createSlotRow(slotsContainer, addSlotButton, day);
        slotsContainer.appendChild(newSlot);
        addSlotButton.disabled = true;
      }
    });

    const defaultSlot = createSlotRow(slotsContainer, addSlotButton, day);
    slotsContainer.appendChild(defaultSlot);
    addSlotButton.disabled = true;

    dayLabel.appendChild(slotsContainer);
    dayLabel.appendChild(addSlotButton);
    availabilitySection.appendChild(dayLabel);
  });
});

function createSlotRow(container, addButtonRef, day) {
  const slotSpan = createElement("span", { class: "slot empty" });

  const startTime = createElement("input", { type: "time" });
  const endTime = createElement("input", { type: "time" });

  const removeButton = createElement(
    "button",
    { type: "button", title: "Remove timeslot" },
    "✖️"
  );

  removeButton.addEventListener("click", () => {
    slotSpan.remove();
    if (addButtonRef) addButtonRef.disabled = false;

    if (teacherData.availability_json[day]) {
      teacherData.availability_json[day] = teacherData.availability_json[
        day
      ].filter((slot) => slot._ref !== slotSpan);
      if (teacherData.availability_json[day].length === 0) {
        delete teacherData.availability_json[day];
      }
    }
  });

  function validateSlot() {
    const startVal = startTime.value;
    const endVal = endTime.value;

    startTime.max = endVal || undefined;
    endTime.min = startVal || undefined;

    startTime.style.border = "";
    endTime.style.border = "";

    if (startVal && endVal && startVal === endVal) {
      alert("Start time and end time cannot be the same.");
      endTime.value = "";
      endTime.focus();
      slotSpan.classList.remove("valid");
      slotSpan.classList.add("empty");
      return;
    }

    const overlapping = isOverlapping(startVal, endVal, slotSpan, container);
    const valid = startVal && endVal && startVal < endVal && !overlapping;

    slotSpan.classList.toggle("valid", valid);
    slotSpan.classList.toggle("empty", !valid);

    if (overlapping) {
      alert("This timeslot overlaps with an existing one.");
      startTime.style.border = "1px solid red";
      endTime.value = "";
      endTime.focus();
    }

    if (valid) {
      if (!teacherData.availability_json[day]) {
        teacherData.availability_json[day] = [];
      }

      teacherData.availability_json[day] = teacherData.availability_json[
        day
      ].filter((slot) => slot._ref !== slotSpan);

      teacherData.availability_json[day].push({
        start: startVal,
        end: endVal,
        _ref: slotSpan, // internal reference
      });
    } else {
      if (teacherData.availability_json[day]) {
        teacherData.availability_json[day] = teacherData.availability_json[
          day
        ].filter((slot) => slot._ref !== slotSpan);
        if (teacherData.availability_json[day].length === 0) {
          delete teacherData.availability_json[day];
        }
      }
    }

    const isEmpty = container.querySelector(".slot.empty");
    if (!isEmpty && addButtonRef) {
      addButtonRef.disabled = false;
    }
  }

  startTime.addEventListener("input", validateSlot);
  endTime.addEventListener("input", validateSlot);

  slotSpan.append(
    startTime,
    document.createTextNode(" - "),
    endTime,
    document.createTextNode(" "),
    removeButton
  );
  return slotSpan;
}

function isOverlapping(start, end, currentSpan, container) {
  const slots = Array.from(container.querySelectorAll(".slot"))
    .filter((span) => span !== currentSpan && span.classList.contains("valid"))
    .map((span) => {
      const [startInput, endInput] = span.querySelectorAll("input");
      return { start: startInput.value, end: endInput.value };
    });

  return slots.some((slot) => !(end <= slot.start || start >= slot.end));
}

function createElement(tag, attrs = {}, text = "") {
  const el = document.createElement(tag);
  Object.entries(attrs).forEach(([key, val]) => el.setAttribute(key, val));
  if (text) el.textContent = text;
  return el;
}
