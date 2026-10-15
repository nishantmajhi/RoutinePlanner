let teachers = {};
let courses = [];

document.addEventListener("DOMContentLoaded", () => {
  Promise.all([
    fetch("/teachers").then((res) => res.json()),
    fetch("/courses").then((res) => res.json()),
  ])
    .then(([teacherData, courseData]) => {
      const excludedSubjectCodes = courseData
        .flatMap((semester) => semester.subjects)
        .filter((subject) => /seminar|project|research/i.test(subject.name))
        .map((subject) => subject.code);

      teachers = Object.values(teacherData).map((teacher) => ({
        ...teacher,
        subjects_array: (teacher.subjects_array || []).filter(
          (code) => !excludedSubjectCodes.includes(code)
        ),
      }));

      courses = courseData.map((semester) => ({
        ...semester,
        subjects: semester.subjects.filter(
          (subject) => !excludedSubjectCodes.includes(subject.code)
        ),
      }));

      renderTeacherList(teachers);
    })
    .catch((error) => {
      console.error(error);
    });
});

function renderTeacherList(teachers) {
  const nav = document.querySelector("aside > nav");
  nav.innerHTML = "";

  teachers.forEach((teacher, index) => {
    const item = createElement("button", { type: "button" }, [
      createElement("small", {}, teacher.salutation),
      createElement("b", {}, teacher.fullName),
    ]);

    item.addEventListener("click", () => {
      document
        .querySelectorAll("aside > nav > button")
        .forEach((el) => el.removeAttribute("aria-current"));
      item.setAttribute("aria-current", "true");
      renderTeacherDetail(teacher);
    });

    nav.appendChild(item);

    if (index === 0) {
      item.setAttribute("aria-current", "true");
      renderTeacherDetail(teacher);
    }
  });
}

function renderTeacherDetail(teacher) {
  const section = document.querySelector("article > section");
  section.innerHTML = "";

  teacher.subjects_array = teacher.subjects_array || [];

  const form = createElement("form", { name: "preferences" }, [
    createElement("h2", {}, `${teacher.salutation} ${teacher.fullName}`),
    renderSubjects(teacher, courses),
    renderAvailability(teacher),
  ]);

  section.appendChild(form);
}

// Example data from backend:
//
// teachers = {
//   "1": {
//     "teacherID": 1,
//     "salutation": "Dr.",
//     "fullName": "Arjun Singh Saud",
//     "subjects_array": ["CSC529", "CSC637", "CSC644"],
//     "availability_json": {
//       Sunday: [{ start: "10:30", end: "12:00" }, { start: "13:30", end: "15:30" }],
//       Monday: [{ start: "11:00", end: "13:00" }, { start: "15:00", end: "17:00" }],
//     }
//   },
//   ...
// }
//
// courses = [
//   {
//     semester: 1,
//     subjects: [
//       { code: "CSC538", name: "Advanced Operating Systems", type: "compulsory" },
//       ...
//     ],
//   },
//  ...
// ];
