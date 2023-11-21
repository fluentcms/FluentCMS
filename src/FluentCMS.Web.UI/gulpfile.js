/// <binding ProjectOpened='default' />
var gulp = require('gulp');
var run = require('gulp-run');

gulp.task('default', function () {
    return run("npm run watch").exec();
})
