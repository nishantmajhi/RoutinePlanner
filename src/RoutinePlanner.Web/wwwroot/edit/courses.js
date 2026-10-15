function renderSubjects(teacher, courses) {
  const select = createElement("select");
  const output = createElement("output", { style: "display:flex;" });

  select.appendChild(
    createElement(
      "option",
      { disabled: true, selected: true },
      "Select a subject to assign it to the teacher"
    )
  );

  rebuildSelectOptions(select, teacher, courses);
  rebuildSubjectCards(output, teacher, courses, select);

  select.addEventListener("change", (event) => {
    const code = event.target.value;
    if (!code) return;

    teacher.subjects_array.push(code);
    updateTeacher({
      teacherID: teacher.teacherID,
      subjects_array: teacher.subjects_array,
    });

    rebuildSelectOptions(select, teacher, courses);
    rebuildSubjectCards(output, teacher, courses, select);
    select.selectedIndex = 0;
  });

  return createElement("section", { class: "subjects" }, [
    createElement("fieldset", {}, [
      createElement("legend", {}, "Subjects"),
      select,
      output,
    ]),
  ]);
}

function addSubjectCard(teacher, code, name, output, select, courses) {
  const removeButton = createElement("button", { type: "button" }, "✖️");

  const card = createElement(
    "span",
    { class: "subject", title: "Click to remove" },
    [document.createTextNode(name + " "), removeButton]
  );

  removeButton.addEventListener("click", () => {
    teacher.subjects_array = teacher.subjects_array.filter((c) => c !== code);
    updateTeacher({
      teacherID: teacher.teacherID,
      subjects_array: teacher.subjects_array,
    });
    rebuildSelectOptions(select, teacher, courses);
    rebuildSubjectCards(output, teacher, courses, select);
  });

  output.appendChild(card);
}

function rebuildSelectOptions(select, teacher, courses) {
  [...select.querySelectorAll("optgroup")].forEach((g) => g.remove());

  const semesterLabels = ["First", "Second", "Third", "Fourth"];

  semesterLabels.forEach((label, index) => {
    const sem = courses.find((s) => s.semester === index + 1);
    if (!sem) return;

    const availableSubjects = sem.subjects
      .filter((sub) => !teacher.subjects_array.includes(sub.code))
      .sort((a, b) => a.code.localeCompare(b.code));

    if (availableSubjects.length === 0) return;

    const optgroup = createElement("optgroup", { label });

    availableSubjects.forEach((subject) => {
      optgroup.appendChild(
        createElement("option", { value: subject.code }, subject.name)
      );
    });

    select.appendChild(optgroup);
  });
}

function rebuildSubjectCards(output, teacher, courses, select) {
  output.innerHTML = "";

  const allSubjects = teacher.subjects_array
    .map((code) => {
      const subject = courses
        .flatMap((sem) => sem.subjects)
        .find((s) => s.code === code);
      if (!subject) return null;

      const semester = courses.find((sem) =>
        sem.subjects.some((s) => s.code === code)
      )?.semester;

      return {
        code,
        name: subject.name,
        semester: semester || 99,
      };
    })
    .filter(Boolean);
    // .sort((a, b) => {
    //   if (a.semester !== b.semester) return a.semester - b.semester;
    //   return a.code.localeCompare(b.code);
    // });

  allSubjects.forEach(({ code, name }) => {
    addSubjectCard(teacher, code, name, output, select, courses);
  });
}
