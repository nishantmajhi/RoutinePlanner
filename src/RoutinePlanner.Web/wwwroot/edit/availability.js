function renderAvailability(teacher) {
  const days = [
    "Sunday",
    "Monday",
    "Tuesday",
    "Wednesday",
    "Thursday",
    "Friday",
    "Saturday",
  ];

  teacher.availability_json = teacher.availability_json || {};

  const fieldset = createElement("fieldset", {}, [
    createElement("legend", {}, "Availability"),
  ]);

  const wrapper = createElement("section", { class: "availability" }, [
    fieldset,
  ]);

  days.forEach((day) => {
    const dayLabel = createElement("label");
    const dayName = createElement("h5", { class: "day" }, day);
    dayLabel.appendChild(dayName);

    const slotsContainer = createElement("div", { class: "slots_container" });
    const slots = teacher.availability_json[day] || [];

    slots.forEach((slot) => {
      const isSaved = slot.start && slot.end;
      slotsContainer.appendChild(
        createSlotRow(
          teacher,
          day,
          slot.start,
          slot.end,
          slotsContainer,
          isSaved
        )
      );
    });

    const addSlotButton = createElement(
      "button",
      { type: "button", title: "Add new timeslot" },
      "➕"
    );

    addSlotButton.addEventListener("click", () => {
      const hasUnsaved = Array.from(
        slotsContainer.querySelectorAll("span")
      ).some((span) => {
        const btn = span.querySelector("button");
        return (
          btn &&
          btn.textContent === "✖️" &&
          btn.title === "Remove unsaved timeslot"
        );
      });

      if (!hasUnsaved) {
        const newSlot = createSlotRow(
          teacher,
          day,
          "",
          "",
          slotsContainer,
          false,
          addSlotButton
        );
        slotsContainer.appendChild(newSlot);
        addSlotButton.disabled = true;
      }
    });

    dayLabel.appendChild(slotsContainer);
    dayLabel.appendChild(addSlotButton);
    fieldset.appendChild(dayLabel);
  });

  return wrapper;
}

function createSlotRow(
  teacher,
  day,
  start = "",
  end = "",
  container,
  isSaved = false,
  addButtonRef = null
) {
  const slotSpan = createElement("span", { class: "slot" });

  const startTime = createElement("input", {
    type: "time",
    value: start,
    max: end || undefined,
  });

  const midSeparator = document.createTextNode(" - ");

  const endTime = createElement("input", {
    type: "time",
    value: end,
    min: start || undefined,
  });

  const endSeparator = document.createTextNode(" ");

  const actionButton = createElement(
    "button",
    {
      type: "button",
      title: isSaved ? "Remove this timeslot" : "Remove unsaved timeslot",
    },
    "✖️"
  );

  const removeSlot = () => {
    const remainingSlots = container.querySelectorAll("span").length;
    if (remainingSlots > 1 || day === "Saturday") {
      slotSpan.remove();
      saveAvailability(teacher, day, container);
      if (addButtonRef) addButtonRef.disabled = false;
    } else {
      alert("At least one slot must remain for each day.");
    }
  };

  actionButton.addEventListener("click", removeSlot);

  function validateSlot() {
    const startVal = startTime.value;
    const endVal = endTime.value;

    startTime.max = endVal || undefined;
    endTime.min = startVal || undefined;

    const valid =
      startVal &&
      endVal &&
      isValidTimeSpan(startTime, endTime) &&
      !isOverlapping(startTime, endTime, slotSpan, container);

    if (!isSaved && valid) {
      const saveButton = createElement(
        "button",
        {
          type: "button",
          title: "Save this timeslot",
        },
        "✔️"
      );

      saveButton.addEventListener("click", () => {
        saveAvailability(teacher, day, container);
        const removeButton = createElement(
          "button",
          {
            type: "button",
            title: "Remove this timeslot",
          },
          "✖️"
        );
        removeButton.addEventListener("click", removeSlot);
        slotSpan.replaceChild(removeButton, saveButton);
        if (addButtonRef) addButtonRef.disabled = false;
      });

      slotSpan.replaceChild(saveButton, actionButton);
    }
  }

  startTime.addEventListener("input", validateSlot);
  endTime.addEventListener("input", validateSlot);

  slotSpan.appendChild(startTime);
  slotSpan.appendChild(midSeparator);
  slotSpan.appendChild(endTime);
  slotSpan.appendChild(endSeparator);
  slotSpan.appendChild(actionButton);

  return slotSpan;
}

function isValidTimeSpan(startTime, endTime) {
  if (startTime.value === endTime.value) {
    alert("Start time and end time cannot be the same.");
    endTime.value = "";
    endTime.focus();
    return false;
  } else if (startTime.value > endTime.value) {
    alert("Start time cannot be larger than end time.");
    endTime.value = "";
    endTime.focus();
    return false;
  } else {
    return true;
  }
}

function isOverlapping(startTime, endTime, currentSpan, container) {
  const currentStart = startTime.value;
  const currentEnd = endTime.value;

  const slots = Array.from(container.querySelectorAll("span"))
    .filter((span) => span !== currentSpan)
    .map((span) => {
      const [startInput, endInput] = span.querySelectorAll("input");
      if (!startInput || !endInput) return null;
      return { start: startInput.value, end: endInput.value };
    })
    .filter(Boolean);

  const overlappingSlot = slots.find((slot) => {
    if (!slot.start || !slot.end) return false;
    return !(currentEnd <= slot.start || currentStart >= slot.end);
  });

  if (overlappingSlot) {
    alert("This timeslot overlaps with an existing one.");
    startTime.style.border = "1px solid red";
    endTime.value = "";
    endTime.focus();
    return true;
  }

  return false;
}

function saveAvailability(teacher, day, container) {
  const slots = Array.from(container.querySelectorAll("span"))
    .map((span) => {
      const [startInput, endInput] = span.querySelectorAll("input");
      if (!startInput || !endInput) return null;

      const start = startInput.value;
      const end = endInput.value;

      return start && end ? (end > start ? { start, end } : null) : null;
    })
    .filter(Boolean);

  teacher.availability_json[day] =
    day === "Saturday" && slots.length === 0 ? null : slots;

  updateTeacher({
    teacherID: teacher.teacherID,
    availability_json: teacher.availability_json,
  });
}
