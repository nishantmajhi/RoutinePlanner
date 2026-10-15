document.addEventListener("DOMContentLoaded", () => {
  const select = document.querySelector("select");
  const output = document.querySelector("output");
  output.innerHTML = "";

  fetch("/courses")
    .then((res) => {
      if (!res.ok) throw new Error("Failed to fetch courses");
      return res.json();
    })
    .then((data) => {
      const semesterLabels = ["First", "Second", "Third", "Fourth"];

      semesterLabels.forEach((label, index) => {
        const sem = data.find((s) => s.semester === index + 1);
        if (!sem) return;

        const subjects = sem.subjects
          .filter(
            (subject) =>
              !/seminar/i.test(subject.name) &&
              !/project/i.test(subject.name) &&
              !/research/i.test(subject.name)
          )
          .sort((a, b) => a.code.localeCompare(b.code));

        if (subjects.length === 0) return;

        const optgroup = document.createElement("optgroup");
        optgroup.label = label;

        subjects.forEach((subject) => {
          const option = document.createElement("option");
          option.value = subject.code;
          option.textContent = subject.name;
          optgroup.appendChild(option);
        });

        select.appendChild(optgroup);
      });
    })
    .catch((err) => {
      console.error("Error loading course data:", err);
      alert("Unable to load courses from the server!");
    });

  select.addEventListener("change", (event) => {
    const selectedOption = select.options[select.selectedIndex];
    const code = selectedOption.value;
    const label = selectedOption.textContent;

    if (!code || teacherData.subjects_array.includes(code)) return;

    teacherData.subjects_array.push(code);
    selectedOption.style.display = "none";

    renderSubjectCard(code, label, output, select);
    output.style.display = "flex";
    select.selectedIndex = 0;

    updateOptgroupVisibility(select);
  });
});

function renderSubjectCard(code, label, output, select) {
  const span = document.createElement("span");
  span.className = "subject";
  span.title = "Click to remove";
  span.textContent = label;

  const button = document.createElement("button");
  button.type = "button";
  button.textContent = "✖️";

  button.addEventListener("click", () => {
    teacherData.subjects_array = teacherData.subjects_array.filter(
      (subject) => subject !== code
    );

    output.removeChild(span);

    const optionToShow = select.querySelector(`option[value="${code}"]`);
    if (optionToShow) optionToShow.style.display = "";

    updateOptgroupVisibility(select);

    if (teacherData.subjects_array.length === 0) {
      output.style.display = "none";
    }
  });

  span.appendChild(button);
  output.appendChild(span);
}

function updateOptgroupVisibility(select) {
  const optgroups = select.querySelectorAll("optgroup");

  optgroups.forEach((group) => {
    const hasVisibleOption = Array.from(group.querySelectorAll("option")).some(
      (option) => option.style.display !== "none"
    );
    group.style.display = hasVisibleOption ? "" : "none";
  });
}